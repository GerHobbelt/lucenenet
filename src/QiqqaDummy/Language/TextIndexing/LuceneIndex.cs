using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers;
using Lucene.Net.Search;
using Utilities.BibTex.Parsing;
using Utilities.Files;
using Directory = Alphaleonis.Win32.Filesystem.Directory;
using File = Alphaleonis.Win32.Filesystem.File;
using Path = Alphaleonis.Win32.Filesystem.Path;
using Version = Lucene.Net.Util.Version;


namespace Utilities.Language.TextIndexing
{
    public class LuceneIndex : IDisposable
    {
        static readonly string INDEX_VERSION = "81.0";  // old Qiqqa indexes were 4.0; this will nuke thm and rebuild. Reverting to oldr Qiqqa will do the same to *us*: delete + rebuild.
        readonly string LIBRARY_INDEX_BASE_PATH;
        Lucene.Net.Store.FSDirectory LIBRARY_INDEX_DIRECTORY;

        Analyzer analyzer;
        object index_writer_lock = new object();
        IndexWriter index_writer = null;

        // Note on IndexWriter --> IndexReader:
        //
        /// 
        /// Expert: returns a readonly reader, covering all
        /// committed as well as un-committed changes to the index.
        /// this provides "near real-time" searching, in that
        /// changes made during an <see cref="IndexWriter"/> session can be
        /// quickly made available for searching without closing
        /// the writer nor calling <see cref="Commit()"/>.
        ///
        /// <para>Note that this is functionally equivalent to calling
        /// Flush() and then opening a new reader.  But the turnaround time of this
        /// method should be faster since it avoids the potentially
        /// costly <see cref="Commit()"/>.</para>
        ///
        /// <para>You must close the <see cref="IndexReader"/> returned by
        /// this method once you are done using it.</para>
        ///
        /// <para>It's <i>near</i> real-time because there is no hard
        /// guarantee on how quickly you can get a new reader after
        /// making changes with <see cref="IndexWriter"/>.  You'll have to
        /// experiment in your situation to determine if it's
        /// fast enough.  As this is a new and experimental
        /// feature, please report back on your findings so we can
        /// learn, improve and iterate.</para>
        ///
        /// <para>The resulting reader supports
        /// <see cref="DirectoryReader.DoOpenIfChanged()"/>, but that call will simply forward
        /// back to this method (though this may change in the
        /// future).</para>
        ///
        /// <para>The very first time this method is called, this
        /// writer instance will make every effort to pool the
        /// readers that it opens for doing merges, applying
        /// deletes, etc.  This means additional resources (RAM,
        /// file descriptors, CPU time) will be consumed.</para>
        ///
        /// <para>For lower latency on reopening a reader, you should
        /// set <see cref="LiveIndexWriterConfig.MergedSegmentWarmer"/> to
        /// pre-warm a newly merged segment before it's committed
        /// to the index.  This is important for minimizing
        /// index-to-search delay after a large merge.  </para>
        ///
        /// <para>If an AddIndexes* call is running in another thread,
        /// then this reader will only search those segments from
        /// the foreign index that have been successfully copied
        /// over, so far.</para>
        ///
        /// <para><b>NOTE</b>: Once the writer is disposed, any
        /// outstanding readers may continue to be used.  However,
        /// if you attempt to reopen any of those readers, you'll
        /// hit an <see cref="ObjectDisposedException"/>.</para>
        ///
        /// @lucene.experimental
        /// 
        /// <returns> <see cref="IndexReader"/> that covers entire index plus all
        /// changes made so far by this <see cref="IndexWriter"/> instance
        /// </returns>
        /// <exception cref="IOException"> If there is a low-level I/O error </exception>
        //
        //public virtual DirectoryReader GetReader(bool applyAllDeletes)


        protected static FieldType STORE_NO_INDEX_ANALYZED = Field.TranslateFieldType(Lucene.Net.Documents.Field.Store.NO, Lucene.Net.Documents.Field.Index.ANALYZED, TermVector.NO);
        protected static FieldType STORE_YES_INDEX_NO_NORMS = Field.TranslateFieldType(Field.Store.YES, Field.Index.NOT_ANALYZED_NO_NORMS, TermVector.NO);

        public LuceneIndex(string LIBRARY_INDEX_BASE_PATH)
        {
            this.LIBRARY_INDEX_BASE_PATH = LIBRARY_INDEX_BASE_PATH;

            CheckIndexVersion();

            // Write the version of the index
            Directory.CreateDirectory(LIBRARY_INDEX_BASE_PATH);
            File.WriteAllText(VersionFilename, INDEX_VERSION);

            // Delete any old locks
            if (File.Exists(LuceneWriteLockFilename))
            {
                Logging.Warn("The lucene file lock was still there (bad shutdown perhaps) - so deleting it");
                File.Delete(LuceneWriteLockFilename);
            }

            // Create our common parts
            analyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.LuceneVersion.LUCENE_29, new Hashtable());

            LIBRARY_INDEX_DIRECTORY = Lucene.Net.Store.FSDirectory.Open(LIBRARY_INDEX_BASE_PATH);
        }

        ~LuceneIndex()
        {
            Logging.Debug("~LuceneIndex()");
            Dispose(false);
        }

        public void Dispose()
        {
            Logging.Debug("Disposing LuceneIndex");
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private int dispose_count = 0;
        protected virtual void Dispose(bool disposing)
        {
            Logging.Debug("LuceneIndex::Dispose({0}) @{1}", disposing, dispose_count);

            try
            {
                if (dispose_count == 0)
                {
                    // Get rid of managed resources
                    Logging.Info("Disposing the lucene index writer");

                    // Utilities.LockPerfTimer l1_clk = Utilities.LockPerfChecker.Start();
                    lock (index_writer_lock)
                    {
                        // l1_clk.LockPerfTimerStop();
                        FlushIndexWriter_LOCK();
                    }
                }

                // Utilities.LockPerfTimer l2_clk = Utilities.LockPerfChecker.Start();
                lock (index_writer_lock)
                {
                    // l2_clk.LockPerfTimerStop();
                    index_writer = null;
                }
            }
            catch (Exception ex)
            {
                Logging.Error(ex);
            }

            ++dispose_count;
        }

        public void WriteMasterList()
        {
            // Utilities.LockPerfTimer l1_clk = Utilities.LockPerfChecker.Start();
            lock (index_writer_lock)
            {
                // l1_clk.LockPerfTimerStop();
                FlushIndexWriter_LOCK();
            }
        }

        private void FlushIndexWriter_LOCK()
        {
            Stopwatch clk = Stopwatch.StartNew();

            Logging.Info("+Flushing a lucene IndexWriter");
            if (null != index_writer)
            {
                index_writer.Commit();
                //index_writer.Optimize();
                //index_writer.Close();
                index_writer.Dispose();
                index_writer = null;
            }
            Logging.Info("-Flushing a lucene IndexWriter (time spent: {0} ms)", clk.ElapsedMilliseconds);
        }

        private static void AddDocumentMetadata_SB(Document document, StringBuilder sb, string field_name, string field_value)
        {
            if (!String.IsNullOrEmpty(field_value))
            {
                sb.AppendLine(field_value);

                document.Add(new Lucene.Net.Documents.Field(field_name, field_value, STORE_NO_INDEX_ANALYZED));
            }
        }

        private static void AddDocumentMetadata_BibTex(Document document, Utilities.BibTex.Parsing.BibTexItem bibtex_item)
        {
            if (null == bibtex_item) return;

            document.Add(new Lucene.Net.Documents.Field("type", bibtex_item.Type, STORE_NO_INDEX_ANALYZED));
            document.Add(new Lucene.Net.Documents.Field("key", bibtex_item.Key, STORE_NO_INDEX_ANALYZED));

            foreach (KeyValuePair<string, string> pair in bibtex_item.EnumerateFields())
            {
                document.Add(new Lucene.Net.Documents.Field(pair.Key, pair.Value, STORE_NO_INDEX_ANALYZED));
            }
        }

        // TODO: refactor call interface: way too many parameters to be legible.
        public void AddDocumentMetadata(bool is_deleted, string fingerprint, string title, string author, string year, string comment, string tag, string annotation, string bibtex, Utilities.BibTex.Parsing.BibTexItem bibtex_item)
        {
            Lucene.Net.Documents.Document document = null;

            // Create the document only if it is not to be deleted
            if (!is_deleted)
            {
                document = new Lucene.Net.Documents.Document();
                document.Add(new Field("fingerprint", fingerprint, STORE_YES_INDEX_NO_NORMS));
                document.Add(new Field("page", "0", STORE_YES_INDEX_NO_NORMS));

                StringBuilder content_sb = new StringBuilder();

                AddDocumentMetadata_SB(document, content_sb, "title", title);
                AddDocumentMetadata_SB(document, content_sb, "author", author);
                AddDocumentMetadata_SB(document, content_sb, "year", year);
                AddDocumentMetadata_SB(document, content_sb, "comment", comment);
                AddDocumentMetadata_SB(document, content_sb, "tag", tag);
                AddDocumentMetadata_SB(document, content_sb, "annotation", annotation);
                AddDocumentMetadata_SB(document, content_sb, "bibtex", bibtex);

                AddDocumentMetadata_BibTex(document, bibtex_item);

                string content = content_sb.ToString();
                document.Add(new Field("content", content, STORE_NO_INDEX_ANALYZED));
            }

            AddDocumentPage_INTERNAL(fingerprint, 0, document);
        }

        public void AddDocumentPage(bool is_deleted, string fingerprint, int page, string content)
        {
            Lucene.Net.Documents.Document document = null;

            // Create the document only if it is not to be deleted
            if (!is_deleted)
            {
                document = new Lucene.Net.Documents.Document();
                document.Add(new Field("fingerprint", fingerprint, STORE_YES_INDEX_NO_NORMS));
                document.Add(new Field("page", Convert.ToString(page), STORE_YES_INDEX_NO_NORMS));
                document.Add(new Field("content", content, STORE_NO_INDEX_ANALYZED));
            }

            AddDocumentPage_INTERNAL(fingerprint, page, document);
        }

        private void AddDocumentPage_INTERNAL(string fingerprint, int page, Document document)
        {
            // Write to the index            
            // Utilities.LockPerfTimer l1_clk = Utilities.LockPerfChecker.Start();
            lock (index_writer_lock)
            {
                // l1_clk.LockPerfTimerStop();
                if (null == index_writer)
                {
                    Logging.Info("+Creating a new lucene IndexWriter");
                    /// Creates a new config that with defaults that match the specified
                    /// <see cref="LuceneVersion"/> as well as the default 
                    /// <see cref="Analyzer"/>. If <paramref name="matchVersion"/> is &gt;= 
                    /// <see cref="LuceneVersion.LUCENE_32"/>, <see cref="TieredMergePolicy"/> is used
                    /// for merging; else <see cref="LogByteSizeMergePolicy"/>.
                    /// Note that <see cref="TieredMergePolicy"/> is free to select
                    /// non-contiguous merges, which means docIDs may not
                    /// remain monotonic over time.  If this is a problem you
                    /// should switch to <see cref="LogByteSizeMergePolicy"/> or
                    /// <see cref="LogDocMergePolicy"/>.
                    IndexWriterConfig config = new IndexWriterConfig(Lucene.Net.Util.LuceneVersion.LUCENE_CURRENT, analyzer);
                    // ??? MaxFieldLength.UNLIMITED
                    index_writer = new Lucene.Net.Index.IndexWriter(LIBRARY_INDEX_DIRECTORY, config);
                    Logging.Info("-Creating a new lucene IndexWriter");
                }

                // Delete the document if it already exists
                Lucene.Net.Search.BooleanQuery bq = new Lucene.Net.Search.BooleanQuery();
                bq.Add(new Lucene.Net.Search.TermQuery(new Lucene.Net.Index.Term("fingerprint", fingerprint)), Lucene.Net.Search.Occur.MUST);
                bq.Add(new Lucene.Net.Search.TermQuery(new Lucene.Net.Index.Term("page", System.Convert.ToString(page))), Lucene.Net.Search.Occur.MUST);
                index_writer.DeleteDocuments(bq);

                // Add the new document
                if (null != document)
                {
                    index_writer.AddDocument(document);
                }
            }
        }

        public int GetDocumentCountForKeyword(string keyword)
        {
            HashSet<string> docs = GetDocumentsWithWord(keyword);
            return docs.Count;
        }


        /***
         * Understands the lucene query syntax
         */
        public List<Utilities.Language.TextIndexing.IndexResult> GetDocumentsWithQuery(string query)
        {
            List<Utilities.Language.TextIndexing.IndexResult> fingerprints = new List<Utilities.Language.TextIndexing.IndexResult>();
            HashSet<string> fingerprints_already_seen = new HashSet<string>();

            try
            {
                using (DirectoryReader index_reader = DirectoryReader.OpenIfChanged(LIBRARY_INDEX_DIRECTORY))
                {
                    /// IndexSearcher:
                    /// 
                    /// Implements search over a single <see cref="Index.IndexReader"/>.
                    ///
                    /// <para/>Applications usually need only call the inherited
                    /// <see cref="Search(Query,int)"/>
                    /// or <see cref="Search(Query,Filter,int)"/> methods. For
                    /// performance reasons, if your index is unchanging, you
                    /// should share a single <see cref="IndexSearcher"/> instance across
                    /// multiple searches instead of creating a new one
                    /// per-search.  If your index has changed and you wish to
                    /// see the changes reflected in searching, you should
                    /// use <see cref="Index.DirectoryReader.OpenIfChanged(Index.DirectoryReader)"/>
                    /// to obtain a new reader and
                    /// then create a new <see cref="IndexSearcher"/> from that.  Also, for
                    /// low-latency turnaround it's best to use a near-real-time
                    /// reader (<see cref="Index.DirectoryReader.Open(Index.IndexWriter,bool)"/>).
                    /// Once you have a new <see cref="Index.IndexReader"/>, it's relatively
                    /// cheap to create a new <see cref="IndexSearcher"/> from it.
                    ///
                    /// <para/><a name="thread-safety"></a><p><b>NOTE</b>: 
                    /// <see cref="IndexSearcher"/> instances are completely
                    /// thread safe, meaning multiple threads can call any of its
                    /// methods, concurrently.  If your application requires
                    /// external synchronization, you should <b>not</b>
                    /// synchronize on the <see cref="IndexSearcher"/> instance;
                    /// use your own (non-Lucene) objects instead.</p>
                    using (Lucene.Net.Search.IndexSearcher index_searcher = new Lucene.Net.Search.IndexSearcher(index_reader))
                    {
                        Lucene.Net.QueryParsers.QueryParser query_parser = new Lucene.Net.QueryParsers.QueryParser(Lucene.Net.Util.LuceneVersion.LUCENE_29, "content", analyzer);

                        Lucene.Net.Search.Query query_object = query_parser.Parse(query);
                        Lucene.Net.Search.Hits hits = index_searcher.Search(query_object);

                        var i = hits.Iterator();
                        while (i.MoveNext())
                        {
                            Lucene.Net.Search.Hit hit = (Lucene.Net.Search.Hit)i.Current;
                            string fingerprint = hit.Get("fingerprint");
                            string page = hit.Get("page");

                            if (!fingerprints_already_seen.Contains(fingerprint))
                            {
                                fingerprints_already_seen.Add(fingerprint);

                                IndexResult index_result = new IndexResult { fingerprint = fingerprint, score = hit.GetScore() };
                                fingerprints.Add(index_result);
                            }
                        }

                        // Close the index
                        index_searcher.Close();
                    }
                    index_reader.Close();
                }
            }
            catch (Exception ex)
            {
                Logging.Warn(ex, "GetDocumentsWithQuery: There was a problem opening the index file for searching.");
            }

            return fingerprints;
        }

        public List<IndexPageResult> GetDocumentPagesWithQuery(string query)
        {
            List<IndexPageResult> results = new List<IndexPageResult>();
            Dictionary<string, IndexPageResult> fingerprints_already_seen = new Dictionary<string, IndexPageResult>();

            try
            {
                using (DirectoryReader index_reader = DirectoryReader.Open(LIBRARY_INDEX_DIRECTORY))
                {
                    using (IndexSearcher index_searcher = new IndexSearcher(index_reader))
                    {
                        QueryParser query_parser = new QueryParser(Lucene.Net.Util.LuceneVersion.LUCENE_29, "content", analyzer);

                        Query query_object = query_parser.Parse(query);
                        Lucene.Net.Search.Hits hits = index_searcher.Search(query_object);

                        var i = hits.Iterator();
                        while (i.MoveNext())
                        {
                            Hit hit = (Hit)i.Current;
                            string fingerprint = hit.Get("fingerprint");
                            int page = Convert.ToInt32(hit.Get("page"));
                            double score = hit.GetScore();

                            // If this is the first time we have seen this fingerprint, make the top-level record
                            if (!fingerprints_already_seen.ContainsKey(fingerprint))
                            {
                                IndexPageResult result = new IndexPageResult();
                                result.fingerprint = fingerprint;
                                result.score = score;

                                // Add to our structures
                                results.Add(result);
                                fingerprints_already_seen[fingerprint] = result;
                            }

                            // And add the page record
                            {
                                IndexPageResult result = fingerprints_already_seen[fingerprint];
                                result.page_results.Add(new PageResult { page = page, score = score });
                            }
                        }

                        // Close the index
                        index_searcher.Close();
                    }
                    index_reader.Close();
                }
            }
            catch (Exception ex)
            {
                Logging.Warn(ex, "GetDocumentPagesWithQuery: There was a problem opening the index file for searching.");
            }

            return results;
        }

        public HashSet<string> GetDocumentsWithWord(string keyword)
        {
            HashSet<string> fingerprints = new HashSet<string>();

            try
            {
                keyword = ReasonableWord.MakeReasonableWord(keyword);
                if (null != keyword)
                {
                    ////Do a quick check for whether there are actually any segments files, otherwise we throw many exceptions in the IndexReader.Open in a very tight loop.
                    ////Added by Nik to cope with some exception...will uncomment this when i know what the problem is...
                    //var segments_files = Directory.GetFiles(LIBRARY_INDEX_BASE_PATH, "segments*", SearchOption.AllDirectories);
                    //if (segments_files.Length <= 0)
                    //{
                    //    Logging.Debug("No index segments files found");
                    //    return fingerprints;
                    //}

                    using (DirectoryReader index_reader = DirectoryReader.Open(LIBRARY_INDEX_DIRECTORY))
                    {
                        using (IndexSearcher index_searcher = new IndexSearcher(index_reader))
                        {
                            TermQuery term_query = new TermQuery(new Term("content", keyword));
                            Hits hits = index_searcher.Search(term_query);

                            var i = hits.Iterator();
                            while (i.MoveNext())
                            {
                                Hit hit = (Hit)i.Current;
                                string fingerprint = hit.Get("fingerprint");
                                fingerprints.Add(fingerprint);
                            }

                            // Close the index
                            index_searcher.Close();
                        }
                        index_reader.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.Warn(ex, "GetDocumentsWithWord: There was a problem opening the index file for searching.");
            }

            return fingerprints;
        }

        public List<string> GetDocumentsSimilarToDocument(string document_filename)
        {
            List<string> fingerprints = new List<string>();

            try
            {
                using (DirectoryReader index_reader = DirectoryReader.Open(LIBRARY_INDEX_DIRECTORY))
                {
                    using (IndexSearcher index_searcher = new IndexSearcher(index_reader))
                    {
                        LuceneMoreLikeThis mlt = new LuceneMoreLikeThis(index_reader);
                        mlt.SetFieldNames(new string[] { "content" });
                        mlt.SetMinTermFreq(0);

                        Query query = mlt.Like(new StreamReader(document_filename));
                        Hits hits = index_searcher.Search(query);
                        var i = hits.Iterator();
                        while (i.MoveNext())
                        {
                            Hit hit = (Hit)i.Current;
                            string fingerprint = hit.Get("fingerprint");
                            fingerprints.Add(fingerprint);
                        }

                        // Close the index
                        index_searcher.Close();
                    }
                    index_reader.Close();
                }
            }
            catch (Exception ex)
            {
                Logging.Warn(ex, "GetDocumentsSimilarToDocument: There was a problem opening the index file for searching.");
            }

            return fingerprints;
        }

        // ---------------------------------------------------------

        private string VersionFilename => Path.GetFullPath(Path.Combine(LIBRARY_INDEX_BASE_PATH, @"index_version.txt"));

        private string LuceneWriteLockFilename => Path.GetFullPath(Path.Combine(LIBRARY_INDEX_BASE_PATH, @"write.lock"));

        public void InvalidateIndex()
        {
            Logging.Warn("Invalidating Lucene index at {0}", LIBRARY_INDEX_BASE_PATH);
            FileTools.Delete(VersionFilename);
        }


        private void CheckIndexVersion()
        {
            string version = String.Empty;

            try
            {
                if (File.Exists(VersionFilename))
                {
                    string[] index_version_lines = File.ReadAllLines(VersionFilename);
                    version = index_version_lines[0].Trim();
                }
            }
            catch (Exception ex)
            {
                Logging.Error(ex, "There was a problem while trying to check the index version");
            }

            if (0 != String.Compare(version, INDEX_VERSION))
            {
                Logging.Warn("This index is out of date (it's version is {0}), so deleting the index.", version);
                DeleteIndex();
            }
        }

        private void DeleteIndex()
        {
            Logging.Info("Deleting the index at path '{0}'", LIBRARY_INDEX_BASE_PATH);
            Utilities.Files.DirectoryTools.DeleteDirectory(LIBRARY_INDEX_BASE_PATH, true);
        }
    }
}
