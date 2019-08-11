using System;
using System.Text;

namespace Utilities
{
    public class Logging
    {
        static Logging()
        {
            Info("Logging initialised");
        }

        private static string AppendStackTrace(string message)
        {
            // Do not append a StackTrace when there's already one:
            if (message.Contains("  at "))
            {
                return message;
            }
            // Do not append stacktrace to every log line: only specific
            // log messages should be augmented with a stacktrace:
            if (message.Contains("Object reference not set to an instance of an object") 
                || message.Contains("Logging initialised"))
            {
                string t = Environment.StackTrace;
                return message + "\n  Stacktrace:\n    " + t.Replace("\n", "\n    ");
            }
            return message;
        }

        private static string PrefixMemUsage(string message)
        {
            return String.Format("[{0:0.000}M] {1}", ((double)GC.GetTotalMemory(false)) / 1E6, message);
        }

        public static void Debug(string msg)
        {
            Console.WriteLine(PrefixMemUsage(AppendStackTrace(msg)));
        }

        public static string Debug(string msg, params object[] args)
        {
            string message = String.Format(msg, args);
            Console.WriteLine(PrefixMemUsage(AppendStackTrace(message)));
            return message;
        }

        public static string Debug(Exception ex)
        {
            string message = ex.ToString();
            Console.WriteLine(PrefixMemUsage(AppendStackTrace(message)));
            return message;
        }

        public static string Debug(Exception ex, string msg, params object[] args)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat(msg, args);
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(ex.ToString());
            string message = sb.ToString();
            Console.WriteLine(PrefixMemUsage(AppendStackTrace(message)));
            return message;
        }

        public static void Info(string msg)
        {
            Console.WriteLine(PrefixMemUsage(AppendStackTrace(msg)));
        }

        public static string Info(string msg, params object[] args)
        {
            string message = String.Format(msg, args);
            Console.WriteLine(PrefixMemUsage(AppendStackTrace(message)));
            return message;
        }

        public static string Warn(string msg, params object[] args)
        {
            string message = String.Format(msg, args);
            Console.WriteLine(PrefixMemUsage(AppendStackTrace(message)));
            return message;
        }

        public static string Warn(Exception ex, string msg, params object[] args)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat(msg, args);
            sb.AppendLine();
            sb.Append(ex.ToString());
            string message = sb.ToString();
            Console.WriteLine(PrefixMemUsage(AppendStackTrace(message)));
            return message;
        }

        public static string Error(string msg, params object[] args)
        {
            string message = String.Format(msg, args);
            Console.WriteLine(PrefixMemUsage(AppendStackTrace(message)));
            return message;
        }

        public static string Error(Exception ex)
        {
            string message = ex.ToString();
            Console.WriteLine(PrefixMemUsage(AppendStackTrace(message)));
            return message;
        }

        public static string Error(Exception ex, string msg, params object[] args)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat(msg, args);
            sb.AppendLine();
            sb.AppendLine();
            sb.Append(ex.ToString());
            string message = sb.ToString();
            Console.WriteLine(PrefixMemUsage(AppendStackTrace(message)));
            return message;
        }
    }
}
