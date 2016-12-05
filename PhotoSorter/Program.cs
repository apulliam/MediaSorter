using System;
using System.IO;
using ExifLib;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Linq;
using System.Security.AccessControl;

namespace PhotoSorter
{
    class PhotoSorter
    {
        private static string _sourceFolder = null;
        private static string _destFolder = null;

        private static string[] _supportedExtensions = {
            ".jpg", ".jpeg", ".tif"
        };

        static void Main(string[] args)
        {
            try
            {
                if (args.Length == 0)
                    throw new ArgumentException("sourceFolder is required");
                _sourceFolder = args[0];
                if (!Directory.Exists(_sourceFolder))
                    throw new ArgumentException("sourceFolder must already exist");

                if (args.Length == 2)
                {
                    _destFolder = args[1];
                    if (!Directory.Exists(_destFolder))
                        Directory.CreateDirectory(_destFolder);
                }
                if (_destFolder == null)
                    _destFolder = _sourceFolder;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                Console.WriteLine("\nPhotoSort <sourceFolder> [<destFolder>]");
                Console.WriteLine("<destFolder> = <sourceFolder> if <destFolder> is omitted");
            }
            if (_sourceFolder != null)
            {
                ProcessFolder(_sourceFolder);
            }
        }

        static void ProcessFolder(string folder)
        {
            foreach (var fileSystemEntry in Directory.EnumerateFileSystemEntries(folder))
            {
                if (Directory.Exists(fileSystemEntry))
                {
                    var directoryInfo = new DirectoryInfo(fileSystemEntry);
                    // ignore "duplicates" directory so that same directories can be reprocessed
                    if (!directoryInfo.Name.Equals("duplicates", StringComparison.InvariantCultureIgnoreCase))
                        ProcessFolder(fileSystemEntry);
                }
                else
                {
                    var fileInfo = new FileInfo(fileSystemEntry);
                    if (_supportedExtensions.Contains(fileInfo.Extension.ToLower()))
                    {
                        DateTime dateTime;
                        string model;

                        using (var reader = new ExifReader(fileSystemEntry))
                        {
                            if (!reader.GetTagValue(ExifTags.DateTimeOriginal, out dateTime))
                                continue;
                            reader.GetTagValue(ExifTags.Model, out model);
                        }
                        var dateFolder = dateTime.ToString("yyyy-MM-dd");
                        var newFolder = Path.Combine(_destFolder, dateFolder, model);

                        Directory.CreateDirectory(newFolder);
                        var newPath = Path.Combine(newFolder, Path.GetFileName(fileSystemEntry));

                        // skip files already in the right place to allow reprocessing directories
                        if (newPath.Equals(fileSystemEntry))
                            continue;

                        if (!File.Exists(newPath))
                        {
                            File.Move(fileSystemEntry, newPath);
                        }
                        else  // handle duplicates
                        {
                            var copyCount = 1;

                            do
                            {
                                var newFileName = string.Format("{0} ({1}){2}", fileInfo.Name, copyCount++, Path.GetExtension(fileSystemEntry));
                                newFolder = Path.Combine(new string[] { _destFolder, dateFolder, model, "duplicates" });
                                newPath = Path.Combine(newFolder, newFileName);
                            }
                            while (File.Exists(newPath));
                            Directory.CreateDirectory(newFolder);
                            File.Move(fileSystemEntry, newPath);
                        }

                        var oldPath = Path.GetDirectoryName(fileSystemEntry);
                        var oldFolders = oldPath.Split(Path.DirectorySeparatorChar);
                        if (oldFolders != null && oldFolders.Length > 0)
                        {
                            // Log old directory if it contains any info other than date
                            var oldParentFolder = oldFolders[oldFolders.Length - 1];
                            if (!oldParentFolder.Equals(dateFolder))
                            {
                                var changeNote = Path.Combine(newFolder, Path.GetFileNameWithoutExtension(newPath) + ".txt");
                                using (var fileStream = new StreamWriter(changeNote))
                                {
                                    fileStream.WriteLine("Orginal path =  " + fileSystemEntry);
                                }
                            }

                            if (!Directory.EnumerateFileSystemEntries(oldPath).Any())
                                Directory.Delete(oldPath);
                        }
                    }
                }
            }
        }
    }
}
