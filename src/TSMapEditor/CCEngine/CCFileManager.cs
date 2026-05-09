using Rampastring.Tools;
using System;
using System.Collections.Generic;
using System.IO;
using TSMapEditor.Settings;

namespace TSMapEditor.CCEngine
{
    public class CCFileManager
    {
        public string GameDirectory { get; set; }
        
        private List<string> searchDirectories = new();

        /// <summary>
        /// Contains information on which MIX file each found game file can be loaded from.
        /// </summary>
        private Dictionary<uint, FileLocationInfo> fileLocationInfos = new();

        /// <summary>
        /// List of all MIX files that have been registered with the file manager.
        /// </summary>
        private List<MixFile> mixFiles = new();

        /// <summary>
        /// List of all CSF files that have been registered with the file manager.
        /// </summary>
        public List<CsfFile> CsfFiles { get; } = new();

        public void ReadConfig()
        {
            var iniFile = Helpers.ReadConfigINI("FileManagerConfig.ini");

            AddSearchDirectory(Environment.CurrentDirectory);
            iniFile.DoForEveryValueInSection("SearchDirectories", v => AddSearchDirectory(Path.Combine(GameDirectory, v)));
            iniFile.DoForEveryValueInSection("MIXFiles", ProcessMixFileEntry);
            iniFile.DoForEveryValueInSection("StringTables", LoadStringTable);

            // Auto-extract MO rules file from expandmo99.mix if it doesn't exist on disk
            ExtractMORulesIfNeeded();
        }

        /// <summary>
        /// Adds a directory to the list of directories where files will be
        /// searched from.
        /// </summary>
        /// <param name="path">The path to the directory.</param>
        public void AddSearchDirectory(string path)
        {
            searchDirectories.Add(Helpers.NormalizePath(path));
        }

        /// <summary>
        /// Processes an entry in the MIXFiles list.
        /// Loads a required or optional MIX file
        /// or handles a special entry.
        /// </summary>
        /// <param name="entry">Contents of an entry of the MIXFiles list.</param>
        private void ProcessMixFileEntry(string entry)
        {
            var parts = entry.Split(',', StringSplitOptions.TrimEntries);
            string mixName = parts[0];

            if (IsSpecialMixName(mixName))
            {
                HandleSpecialMixName(mixName);
                return;
            }

            bool isRequired = false;

            if (parts.Length > 1)
                isRequired = Conversions.BooleanFromString(parts[1], isRequired);

            if (isRequired)
                LoadRequiredMixFile(mixName);
            else
                LoadOptionalMixFile(mixName);
        }

        /// <summary>
        /// Loads a MIX file.
        /// Searches for it from both the search directories
        /// as well as already loaded MIX files.
        /// </summary>
        /// <param name="name">The name of the MIX file.</param>
        /// <returns>True if the MIX file was successfully loaded, otherwise false.</returns>
        private bool LoadMixFile(string name)
        {
            uint identifier = MixFile.GetFileID(name);

            // First check from game directory, if not found then check from already loaded MIX files
            if (!LoadMixFromDirectories(name))
            {
                if (fileLocationInfos.TryGetValue(identifier, out FileLocationInfo value))
                {
                    // Logger.Log("Loading MIX file " + name + " from existing MIX file " + Path.GetFileName(value.MixFile.FilePath));
                    var mixFile = new MixFile(value.MixFile, value.Offset);
                    mixFile.Parse();
                    AddMix(mixFile);
                    return true;
                }

                Logger.Log("Failed to find MIX file: " + name);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Attempts to search for and load a MIX file from the search directories.
        /// Returns true if loading the MIX file succeeds, otherwise false.
        /// </summary>
        /// <param name="name">The name of the MIX file.</param>
        /// <returns>True if the MIX file was successfully loaded, otherwise false.</returns>
        private bool LoadMixFromDirectories(string name)
        {
            string searchDir = null;

            foreach (string dir in searchDirectories)
            {
                if (File.Exists(Path.Combine(dir, name)))
                {
                    searchDir = dir;
                    break;
                }
            }

            if (searchDir == null)
                return false;

            Logger.Log("Loading MIX file " + name + " from " + searchDir);
            var mixFile = new MixFile();
            mixFile.Parse(Path.Combine(searchDir, name));
            AddMix(mixFile);

            return true;
        }

        /// <summary>
        /// Registers a MIX file to the file system.
        /// Adds all file entries from the MIX file to the file location tracking system.
        /// </summary>
        /// <param name="mixFile">The MIX file to register.</param>
        private void AddMix(MixFile mixFile)
        {
            mixFiles.Add(mixFile);

            if (UserSettings.Instance.LogFileLoading)
                Logger.Log("Registering " + mixFile.GetEntries().Count + " file entries from " + Path.GetFileName(mixFile.FilePath));

            foreach (MixFileEntry fileEntry in mixFile.GetEntries())
            {
                if (fileLocationInfos.ContainsKey(fileEntry.Identifier))
                    continue;

                fileLocationInfos[fileEntry.Identifier] = new FileLocationInfo(mixFile, fileEntry.Offset, fileEntry.Size);
            }
        }

        /// <summary>
        /// Loads a required MIX file.
        /// Throws a FileNotFoundException if the MIX file isn't found.
        /// </summary>
        /// <param name="name">The name of the MIX file.</param>
        public void LoadRequiredMixFile(string name)
        {
            if (!LoadMixFile(name))
            {
                throw new FileNotFoundException("Required MIX file not found: " + name);
            }
        }

        /// <summary>
        /// Loads an optional MIX file.
        /// Does not throw an exception if the MIX file is not found.
        /// </summary>
        /// <param name="name">The name of the MIX file.</param>
        public void LoadOptionalMixFile(string name)
        {
            if (!LoadMixFile(name))
            {
                Logger.Log("Optional MIX file not found: " + name);
            }
        }

        /// <summary>
        /// Loads MIX files of the format NAME##.
        /// </summary>
        /// <param name="name">The common name of the MIX files.</param>
        public void LoadIndexedMixFiles(string name)
        {
            for (int i = 99; i >= 0; i--)
                LoadMixFile($"{name}{i:00}.mix");
        }

        /// <summary>
        /// Loads MIX files with a wildcard.
        /// </summary>
        /// <param name="name">The common name of the MIX files.</param>
        public void LoadWildcardMixFiles(string name)
        {
            foreach (string searchDirectory in searchDirectories)
            {
                if (!Directory.Exists(searchDirectory))
                    continue;

                var files = Directory.GetFiles(searchDirectory, name);
                foreach (string file in files)
                    LoadMixFile(Path.GetFileName(file));
            }
        }

        /// <summary>
        /// Loads a required CSF file.
        /// Throws a FileNotFoundException if the CSF file isn't found.
        /// </summary>
        /// <param name="name">The name of the CSf file.</param>
        public void LoadStringTable(string name)
        {
            var data = LoadFile(name);
            if (data == null)
                throw new FileNotFoundException("CSF file not found: " + name);
            var file = new CsfFile(name);
            file.ParseFromBuffer(data);
            CsfFiles.Add(file);
        }

        /// <summary>
        /// Searches for a file from all search directories.
        /// If found, returns the full path to the found file.
        /// Otherwise returns null.
        /// </summary>
        /// <param name="fileName">The name of the file to look for.</param>
        public string FindFileFromDirectories(string fileName)
        {
            foreach (string searchDirectory in searchDirectories)
            {
                string fullPath = Path.Combine(searchDirectory, fileName);

                if (File.Exists(fullPath))
                    return fullPath;
            }

            return null;
        }

        public byte[] LoadFile(string name)
        {
            if (UserSettings.Instance.LogFileLoading)
                Logger.Log("LoadFile: " + name);

            foreach (string searchDirectory in searchDirectories)
            {
                string looseFilePath = Path.Combine(searchDirectory, name);
                if (File.Exists(looseFilePath))
                {
                    var data = File.ReadAllBytes(looseFilePath);
                    if (UserSettings.Instance.LogFileLoading)
                        Logger.Log($"    File found from disk: {searchDirectory} (size={data.Length} bytes)");
                    return data;
                }
            }

            uint id = MixFile.GetFileID(name);

            if (fileLocationInfos.TryGetValue(id, out FileLocationInfo value))
            {
                var data = value.MixFile.GetSingleFileData(value.Offset, value.Size);
                if (UserSettings.Instance.LogFileLoading)
                    Logger.Log($"    File found from MIX: {Path.GetFileName(value.MixFile.FilePath)} (size={data.Length} bytes)");
                return data;
            }

            return null;
        }

        private bool IsSpecialMixName(string name)
        {
            name = name.ToUpper();
            switch (name)
            {
                case "$TSECACHE":
                case "$RA2ECACHE":
                case "$TSELOCAL":
                case "$RA2ELOCAL":
                case "$EXPAND":
                case "$EXPANDMD":
                case "$EXPANDMO":
                    return true;
                default:
                    return false;
            }
        }

        private void HandleSpecialMixName(string name)
        {
            name = name.ToUpper();
            switch (name)
            {
                case "$TSECACHE":
                    LoadIndexedMixFiles("ecache");
                    break;
                case "$RA2ECACHE":
                    LoadWildcardMixFiles("ecache*.mix");
                    break;
                case "$TSELOCAL":
                    LoadIndexedMixFiles("elocal");
                    break;
                case "$RA2ELOCAL":
                    LoadWildcardMixFiles("elocal*.mix");
                    break;
                case "$EXPAND":
                    LoadIndexedMixFiles("expand");
                    break;
                case "$EXPANDMD":
                    LoadIndexedMixFiles("expandmd");
                    break;
                case "$EXPANDMO":
                    LoadWildcardMixFiles("expandmo*.mix");
                    break;
            }
        }

        /// <summary>
        /// Auto-extracts the Mental Omega complete rules file from expandmo99.mix
        /// if it doesn't already exist on disk. MO's rules are stored as an unnamed
        /// entry (hash 0xE8DF0937) inside expandmo99.mix and cannot be loaded by
        /// filename alone, so we extract it once and save it as rulesmo.ini.
        /// </summary>
        private void ExtractMORulesIfNeeded()
        {
            const string moRulesFileName = "rulesmo.ini";
            const uint moRulesHashId = 0xE8DF0937;
            const string moRulesSignature = "Mental Omega";

            // Check if the file already exists on disk in any search directory
            if (FindFileFromDirectories(moRulesFileName) != null)
                return;

            // Try to find the entry by hash ID in loaded MIX files
            if (!fileLocationInfos.TryGetValue(moRulesHashId, out FileLocationInfo info))
                return; // expandmo99.mix not loaded or entry not found — not an MO installation

            // Extract the data
            var data = info.MixFile.GetSingleFileData(info.Offset, info.Size);
            if (data == null || data.Length < 100)
                return;

            // Verify this is actually the MO rules file by checking its header signature
            string header = System.Text.Encoding.ASCII.GetString(data, 0, Math.Min(200, data.Length));
            if (!header.Contains(moRulesSignature))
            {
                Logger.Log($"ExtractMORules: Entry 0x{moRulesHashId:X8} found but does not contain expected MO rules signature. Skipping.");
                return;
            }

            // Save to game directory
            string outputPath = Path.Combine(GameDirectory, moRulesFileName);
            try
            {
                File.WriteAllBytes(outputPath, data);
                Logger.Log($"ExtractMORules: Auto-extracted {moRulesFileName} ({data.Length} bytes) to {outputPath}");
            }
            catch (Exception ex)
            {
                Logger.Log($"ExtractMORules: Failed to save {moRulesFileName}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Struct for holding data on which MIX file a file exists in,
    /// and where the file exists within the MIX file.
    /// </summary>
    internal struct FileLocationInfo
    {
        public FileLocationInfo(MixFile mixFile, int offset, int size)
        {
            MixFile = mixFile;
            Offset = offset;
            Size = size;
        }

        public MixFile MixFile { get; private set; }
        public int Offset { get; private set; }
        public int Size { get; private set; }
    }
}
