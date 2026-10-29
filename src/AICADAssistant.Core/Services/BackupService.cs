using System;
using System.Collections.Generic;
using System.IO;

namespace AICADAssistant.Core.Services
{
    public class CheckpointInfo
    {
        public string Path { get; set; }
        public string OriginalFileName { get; set; }
        public DateTime Timestamp { get; set; }
        public long SizeBytes { get; set; }
    }

    /// <summary>
    /// Manages file backups and checkpoints to ensure model recovery during revisions.
    /// Preserves user files by never overwriting directly without a safe checkpoint.
    /// </summary>
    public class BackupService
    {
        private readonly string _checkpointDirectory;

        public BackupService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _checkpointDirectory = Path.Combine(appData, "AICADAssistant", "checkpoints");
            try
            {
                if (!Directory.Exists(_checkpointDirectory))
                {
                    Directory.CreateDirectory(_checkpointDirectory);
                }
            }
            catch { }
        }

        public BackupService(string customCheckpointDirectory)
        {
            _checkpointDirectory = customCheckpointDirectory;
            if (!Directory.Exists(_checkpointDirectory))
            {
                Directory.CreateDirectory(_checkpointDirectory);
            }
        }

        public string CreateCheckpoint(string sourceFilePath)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
                return null;

            try
            {
                string fileName = Path.GetFileNameWithoutExtension(sourceFilePath);
                string ext = Path.GetExtension(sourceFilePath);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string backupName = string.Format("{0}_cp_{1}{2}", fileName, timestamp, ext);
                string destPath = Path.Combine(_checkpointDirectory, backupName);

                File.Copy(sourceFilePath, destPath, true);
                LoggingService.Instance.Info("Checkpoint berhasil dibuat: " + backupName);

                CleanOldCheckpoints(10);
                return destPath;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal membuat checkpoint untuk: " + sourceFilePath, ex);
                return null;
            }
        }

        public bool RestoreCheckpoint(string checkpointPath, string targetPath)
        {
            if (string.IsNullOrWhiteSpace(checkpointPath) || !File.Exists(checkpointPath))
                return false;

            try
            {
                File.Copy(checkpointPath, targetPath, true);
                LoggingService.Instance.Info(string.Format("Checkpoint dipulihkan dari '{0}' ke '{1}'", Path.GetFileName(checkpointPath), targetPath));
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal memulihkan checkpoint: " + checkpointPath, ex);
                return false;
            }
        }

        public List<CheckpointInfo> ListCheckpoints(string originalFileName = null)
        {
            var result = new List<CheckpointInfo>();
            if (!Directory.Exists(_checkpointDirectory))
                return result;

            try
            {
                string searchPattern = string.IsNullOrWhiteSpace(originalFileName)
                    ? "*.*"
                    : string.Format("{0}_cp_*.*", Path.GetFileNameWithoutExtension(originalFileName));

                var files = Directory.GetFiles(_checkpointDirectory, searchPattern);
                foreach (var file in files)
                {
                    var fi = new FileInfo(file);
                    result.Add(new CheckpointInfo
                    {
                        Path = file,
                        OriginalFileName = fi.Name,
                        Timestamp = fi.CreationTime,
                        SizeBytes = fi.Length
                    });
                }

                result.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("Gagal mendata checkpoint", ex);
            }

            return result;
        }

        public void CleanOldCheckpoints(int keepCount = 10)
        {
            try
            {
                if (!Directory.Exists(_checkpointDirectory)) return;

                var files = new DirectoryInfo(_checkpointDirectory).GetFiles("*_cp_*.*");
                if (files.Length <= keepCount) return;

                Array.Sort(files, (a, b) => b.CreationTime.CompareTo(a.CreationTime));
                for (int i = keepCount; i < files.Length; i++)
                {
                    try { files[i].Delete(); } catch { }
                }
            }
            catch { }
        }
    }
}
