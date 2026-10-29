using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AICADAssistant.Core.Services;

namespace AICADAssistant.Tests
{
    [TestClass]
    public class BackupServiceTests
    {
        private string _tempDir;
        private string _testFile;
        private BackupService _backupService;

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "AICAD_BackupTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _backupService = new BackupService(_tempDir);

            _testFile = Path.Combine(_tempDir, "sample_bracket.SLDPRT");
            File.WriteAllText(_testFile, "MOCK SOLIDWORKS CAD DATA 1.0");
        }

        [TestMethod]
        public void CreateCheckpoint_ValidFile_CreatesBackup()
        {
            string cp = _backupService.CreateCheckpoint(_testFile);
            Assert.IsNotNull(cp);
            Assert.IsTrue(File.Exists(cp));
            Assert.AreEqual("MOCK SOLIDWORKS CAD DATA 1.0", File.ReadAllText(cp));
        }

        [TestMethod]
        public void RestoreCheckpoint_RestoresOriginalContent()
        {
            string cp = _backupService.CreateCheckpoint(_testFile);
            Assert.IsNotNull(cp);

            // Modify original file
            File.WriteAllText(_testFile, "MODIFIED DATA");
            Assert.AreEqual("MODIFIED DATA", File.ReadAllText(_testFile));

            // Restore from checkpoint
            bool ok = _backupService.RestoreCheckpoint(cp, _testFile);
            Assert.IsTrue(ok);
            Assert.AreEqual("MOCK SOLIDWORKS CAD DATA 1.0", File.ReadAllText(_testFile));
        }

        [TestMethod]
        public void ListCheckpoints_ReturnsMatchingList()
        {
            _backupService.CreateCheckpoint(_testFile);
            var list = _backupService.ListCheckpoints("sample_bracket.SLDPRT");
            Assert.IsTrue(list.Count > 0);
        }

        [TestMethod]
        public void CreateCheckpoint_NonExistentFile_ReturnsNull()
        {
            string cp = _backupService.CreateCheckpoint("C:\\path\\does_not_exist_9876.SLDPRT");
            Assert.IsNull(cp);
        }
    }
}

