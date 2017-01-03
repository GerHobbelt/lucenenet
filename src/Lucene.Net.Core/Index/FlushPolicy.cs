using System.Collections.Generic;
using System.Diagnostics;

namespace Lucene.Net.Index
{
    /*
     * Licensed to the Apache Software Foundation (ASF) under one or more
     * contributor license agreements.  See the NOTICE file distributed with
     * this work for additional information regarding copyright ownership.
     * The ASF licenses this file to You under the Apache License, Version 2.0
     * (the "License"); you may not use this file except in compliance with
     * the License.  You may obtain a copy of the License at
     *
     *     http://www.apache.org/licenses/LICENSE-2.0
     *
     * Unless required by applicable law or agreed to in writing, software
     * distributed under the License is distributed on an "AS IS" BASIS,
     * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
     * See the License for the specific language governing permissions and
     * limitations under the License.
     */

    using InfoStream = Lucene.Net.Util.InfoStream;
    using ThreadState = Lucene.Net.Index.DocumentsWriterPerThreadPool.ThreadState;

    /// <summary>
    /// <seealso cref="FlushPolicy"/> controls when segments are flushed from a RAM resident
    /// internal data-structure to the <seealso cref="IndexWriter"/>s <seealso cref="Directory"/>.
    /// <p>
    /// Segments are traditionally flushed by:
    /// <ul>
    /// <li>RAM consumption - configured via
    /// <seealso cref="IndexWriterConfig#setRAMBufferSizeMB(double)"/></li>
    /// <li>Number of RAM resident documents - configured via
    /// <seealso cref="IndexWriterConfig#setMaxBufferedDocs(int)"/></li>
    /// </ul>
    /// The policy also applies pending delete operations (by term and/or query),
    /// given the threshold set in
    /// <seealso cref="IndexWriterConfig#setMaxBufferedDeleteTerms(int)"/>.
    /// <p>
    /// <seealso cref="IndexWriter"/> consults the provided <seealso cref="FlushPolicy"/> to control the
    /// flushing process. The policy is informed for each added or updated document
    /// as well as for each delete term. Based on the <seealso cref="FlushPolicy"/>, the
    /// information provided via <seealso cref="ThreadState"/> and
    /// <seealso cref="DocumentsWriterFlushControl"/>, the <seealso cref="FlushPolicy"/> decides if a
    /// <seealso cref="DocumentsWriterPerThread"/> needs flushing and mark it as flush-pending
    /// via <seealso cref="DocumentsWriterFlushControl#setFlushPending"/>, or if deletes need
    /// to be applied.
    /// </summary>
    /// <seealso cref= ThreadState </seealso>
    /// <seealso cref= DocumentsWriterFlushControl </seealso>
    /// <seealso cref= DocumentsWriterPerThread </seealso>
    /// <seealso cref= IndexWriterConfig#setFlushPolicy(FlushPolicy) </seealso>
    internal abstract class FlushPolicy
    {
        protected LiveIndexWriterConfig m_indexWriterConfig;
        protected InfoStream m_infoStream;

        /// <summary>
        /// Called for each delete term. If this is a delete triggered due to an update
        /// the given <seealso cref="ThreadState"/> is non-null.
        /// <p>
        /// Note: this method is called synchronized on the given
        /// <seealso cref="DocumentsWriterFlushControl"/> and it is guaranteed that the calling
        /// thread holds the lock on the given <seealso cref="ThreadState"/>
        /// </summary>
        public abstract void OnDelete(DocumentsWriterFlushControl control, ThreadState state);

        /// <summary>
        /// Called for each document update on the given <seealso cref="ThreadState"/>'s
        /// <seealso cref="DocumentsWriterPerThread"/>.
        /// <p>
        /// Note: this method is called  synchronized on the given
        /// <seealso cref="DocumentsWriterFlushControl"/> and it is guaranteed that the calling
        /// thread holds the lock on the given <seealso cref="ThreadState"/>
        /// </summary>
        public virtual void OnUpdate(DocumentsWriterFlushControl control, ThreadState state)
        {
            OnInsert(control, state);
            OnDelete(control, state);
        }

        /// <summary>
        /// Called for each document addition on the given <seealso cref="ThreadState"/>s
        /// <seealso cref="DocumentsWriterPerThread"/>.
        /// <p>
        /// Note: this method is synchronized by the given
        /// <seealso cref="DocumentsWriterFlushControl"/> and it is guaranteed that the calling
        /// thread holds the lock on the given <seealso cref="ThreadState"/>
        /// </summary>
        public abstract void OnInsert(DocumentsWriterFlushControl control, ThreadState state);

        /// <summary>
        /// Called by DocumentsWriter to initialize the FlushPolicy
        /// </summary>
        protected internal virtual void Init(LiveIndexWriterConfig indexWriterConfig)
        {
            lock (this)
            {
                this.m_indexWriterConfig = indexWriterConfig;
                m_infoStream = indexWriterConfig.InfoStream;
            }
        }

        /// <summary>
        /// Returns the current most RAM consuming non-pending <seealso cref="ThreadState"/> with
        /// at least one indexed document.
        /// <p>
        /// this method will never return <code>null</code>
        /// </summary>
        protected virtual ThreadState FindLargestNonPendingWriter(DocumentsWriterFlushControl control, ThreadState perThreadState)
        {
            Debug.Assert(perThreadState.dwpt.NumDocsInRAM > 0);
            long maxRamSoFar = perThreadState.bytesUsed;
            // the dwpt which needs to be flushed eventually
            ThreadState maxRamUsingThreadState = perThreadState;
            Debug.Assert(!perThreadState.flushPending, "DWPT should have flushed");
            IEnumerator<ThreadState> activePerThreadsIterator = control.AllActiveThreadStates();
            while (activePerThreadsIterator.MoveNext())
            {
                ThreadState next = activePerThreadsIterator.Current;
                if (!next.flushPending)
                {
                    long nextRam = next.bytesUsed;
                    if (nextRam > maxRamSoFar && next.dwpt.NumDocsInRAM > 0)
                    {
                        maxRamSoFar = nextRam;
                        maxRamUsingThreadState = next;
                    }
                }
            }
            Debug.Assert(AssertMessage("set largest ram consuming thread pending on lower watermark"));
            return maxRamUsingThreadState;
        }

        private bool AssertMessage(string s)
        {
            if (m_infoStream.IsEnabled("FP"))
            {
                m_infoStream.Message("FP", s);
            }
            return true;
        }

        public virtual object Clone()
        {
            FlushPolicy clone;

            clone = (FlushPolicy)base.MemberwiseClone();
            clone.m_indexWriterConfig = null;
            clone.m_infoStream = null;
            return clone;
        }
    }
}