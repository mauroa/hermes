using System;
using System.IO;
using System.Reflection;

namespace Hermes
{
	public class Logger : ILogger
	{
		private static readonly object lockObject = new object ();

		private readonly string logPath;

		public Logger ()
		{
			var directory = Path.Combine (Path.GetTempPath (), "Hermes Logs");

			if (!Directory.Exists (directory)) {
				Directory.CreateDirectory (directory);
			}

			this.logPath = Path.Combine (directory, "HermesLog.txt");
		}

		public void Log (string message, params object[] args)
		{
			lock (lockObject) {
				var line = string.Format("{0} - {1} - {2}{3}", DateTime.Now, Assembly.GetExecutingAssembly().FullName.Substring(7, 6), string.Format (message, args), Environment.NewLine);

				this.WriteLine (line);
			}
		}

		private void WriteLine (string line)
		{
			try {
				File.AppendAllText (this.logPath, line);
			} catch (Exception) {
				this.WriteLine (line);
			}
		}
	}
}