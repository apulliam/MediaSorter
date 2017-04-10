using System;
using System.IO;
using ExifLib;
using System.Security.Cryptography;
using System.Linq;

namespace PhotoSorter
{
    internal class PhotoSorter
    {
        private int _folderCount = 0;
        private int _fileCount = 0;
        private string _sourceFolder = null;
        private string _destFolder = null;
        private bool _keepDuplicates = false;
        private StreamWriter _logFile;

        private static string _duplicatesFolder = "ps-duplicates";
        private static string[] _supportedExtensions = {
            ".jpg", ".jpeg"
        };

        static void Main(string[] args)
        {
            var photoSorter = new PhotoSorter();
            photoSorter.Run(args);
        }

        internal void Run(string[] args)
        {
            try
            {
                if (args.Length == 0)
                    throw new ArgumentException("sourceFolder is required");
                _sourceFolder = args[0];
                if (!System.IO.Directory.Exists(_sourceFolder))
                    throw new ArgumentException("sourceFolder must already exist");

                if (args.Length >= 2)
                {
                    _destFolder = args[1];
                    if (!System.IO.Directory.Exists(_destFolder))
                        System.IO.Directory.CreateDirectory(_destFolder);
                }
                if (_destFolder == null)
                    _destFolder = _sourceFolder;
                if (args.Length == 3)
                    if (args[2].Equals("-keepDuplicates", StringComparison.CurrentCultureIgnoreCase))
                        _keepDuplicates = true;
                    else
                        throw new ArgumentException("Invalid argument:" + args[2]);

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                Console.WriteLine("\nPhotoSort <sourceFolder> [<destFolder>] [-keepDuplicates]");
                Console.WriteLine("<destFolder> = <sourceFolder> if <destFolder> is omitted");
            }
            if (_sourceFolder != null)
            {
                var startTime = DateTime.Now;
                var logFileName = $"PhotoSorter-{startTime:yyyy-MM-dd_hh-mm-ss-tt}.log";
                using (_logFile = new StreamWriter(logFileName))
                {
                    ProcessFolder(_sourceFolder);
                }
                var elapsedTime = DateTime.Now - startTime;
                Console.WriteLine("Processed {0} files in {1} folders, elapsed time = {2}", _fileCount, _folderCount, elapsedTime);
                Console.WriteLine("Details in log file {0}", logFileName);
               
            }
        }

        private void ProcessFolder(string folder)
        {
            foreach (var directory in System.IO.Directory.GetDirectories(folder))
            {
                ++_folderCount;
                var directoryInfo = new DirectoryInfo(directory);
                
                // ignore "duplicates" directory so that same directories can be reprocessed
                if (!directoryInfo.Name.Equals(_duplicatesFolder, StringComparison.InvariantCultureIgnoreCase))
                    ProcessFolder(directory);
            }

            foreach (var file in System.IO.Directory.GetFiles(folder))
            {
                ++_fileCount;
    
                var fileInfo = new FileInfo(file);

                // delete Thumbs.db 
                if (fileInfo.Name.Equals("Thumbs.db"))
                {
                    _logFile.WriteLine("Deleting " + fileInfo.FullName);
                    File.Delete(fileInfo.FullName);
                    continue;
                }

                if (!_supportedExtensions.Contains(fileInfo.Extension.ToLower()))
                {
                    _logFile.WriteLine("Skipping " + file + " - unsupported file type");
                    continue;
                }

                DateTime dateTime, dateTimeOriginal, digitizedTime;
                string make, model;
                   
                try
                {
                    using (var reader = new ExifLib.ExifReader(file))
                    {
                        reader.GetTagValue<DateTime>(ExifTags.DateTimeDigitized, out digitizedTime);
                        var dateTimeFound = reader.GetTagValue<DateTime>(ExifTags.DateTime, out dateTime);
                        var originalDateTimeFound = reader.GetTagValue<DateTime>(ExifTags.DateTimeOriginal, out dateTimeOriginal);
                        if (originalDateTimeFound)
                        {
                            dateTime = dateTimeOriginal;
                        }
                        else
                        {
                            if (!dateTimeFound)
                            {
                                _logFile.WriteLine("Skipping " + file + " - No DateTimeOriginal or DateTime Exif tag");
                                continue;
                            }
                        }
                        reader.GetTagValue<string>(ExifTags.Make, out make);
                        reader.GetTagValue<string>(ExifTags.Model, out model);
                    }
                }
                catch
                {
                    // skip pictures where we can't read Exif tags
                    _logFile.WriteLine("Skipping " + file + " - No Exif tags");
                    continue;
                }

                var camera = string.Empty;
                if (make != null)
                    camera += make;
                if (model != null)
                {
                    // Some camera manufacturers include make in model field
                    if (camera != string.Empty)
                    {
                        if (model.StartsWith(camera))
                            camera = model;
                        else 
                            camera = $"{make} {model}";
                    }
                }
                var year = dateTime.Year.ToString();
                var date = dateTime.ToString("yyyy-MM-dd");
                var newFolder = Path.Combine(_destFolder, year, date, camera);

                System.IO.Directory.CreateDirectory(newFolder);
                var newPath = Path.Combine(newFolder, Path.GetFileName(file));

                // skip files already in the right place to allow reprocessing directories
                if (newPath.Equals(file))
                {
                    _logFile.WriteLine("Skipping " + file + " - already in correct directory");
                }
                else if (!File.Exists(newPath))
                {
                    _logFile.WriteLine("Moving " + file + " to " + newPath);
                    File.Move(file, newPath);
                    WriteOldFilename(fileInfo, date, newPath);
                }
                else  // handle duplicates
                {
                    //  check filesize and checksum to determine if file is really duplicate
                    var newFileInfo = new FileInfo(newPath);
                    var different = fileInfo.Length != newFileInfo.Length || !FilesAreEqual(fileInfo, newFileInfo);
                    if (different)  // always move file to destination directory as copy if the size and checksum are different
                    {
                        newPath = CreateDuplicateFileName(fileInfo, newFolder);
                        _logFile.WriteLine("Moving " + file + " to " + newPath);
                        File.Move(file, newPath);
                        WriteOldFilename(fileInfo, date, newPath);
                    }
                    else if (_keepDuplicates) // for true duplicates only keep if flag set
                    {
                        newFolder = Path.Combine(newFolder, _duplicatesFolder);
                        System.IO.Directory.CreateDirectory(newFolder);
                        newPath = CreateDuplicateFileName(fileInfo, newPath);
                        _logFile.WriteLine("Moving " + file + " to " + newPath);
                        File.Move(file, newPath);
                        WriteOldFilename(fileInfo, date, newPath);
                    }
                    else // default is to delete duplicates
                    {
                        _logFile.WriteLine("Deleting duplicate " + file);
                        File.Delete(file);
                    }
                }
            }

            // Delete empty source directories
            if (!System.IO.Directory.EnumerateFileSystemEntries(folder).Any())
            {
                _logFile.WriteLine("Removing directory " + folder);
                System.IO.Directory.Delete(folder);
            }
        }

        private string CreateDuplicateFileName(FileInfo fileInfo, string folder)
        {
            var copyCount = 1;
            string newPath = null;
            do
            {
                var newFileName = string.Format("{0}({1}){2}", fileInfo.Name, copyCount++, fileInfo.Extension);
                newPath = Path.Combine(folder, newFileName);
            }
            while (File.Exists(newPath));
            return newPath;
        }

        private void WriteOldFilename(FileInfo fileInfo,string dateFolder, string newPath)
        {
            if (!fileInfo.Directory.Name.Equals(dateFolder))
            {
                var newFileInfo = new FileInfo(newPath);
                var changeNote = Path.Combine(newFileInfo.DirectoryName, Path.GetFileNameWithoutExtension(newFileInfo.Name) + ".txt");
                using (var fileStream = new StreamWriter(changeNote))
                {
                    fileStream.WriteLine("Orginal path =  " + fileInfo.FullName);
                }
            }
        }

        private bool FilesAreEqual(FileInfo first, FileInfo second)
        {
            using (var firstFile = first.OpenRead())
            {
                using (var secondFile = second.OpenRead())
                {
                    using (var md5 = MD5.Create())
                    {
                        var firstHash = md5.ComputeHash(firstFile);
                        var secondHash = md5.ComputeHash(secondFile);

                        for (int i = 0; i < firstHash.Length; i++)
                        {
                            if (firstHash[i] != secondHash[i])
                                return false;
                        }
                        return true;
                    }
                }
            }
        }
    }
}
