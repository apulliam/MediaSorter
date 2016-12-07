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
        private static bool _keepDuplicates = false;

        private static string[] _jpegExtensions = {
            ".jpg", ".jpeg"
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

                if (args.Length >= 2)
                {
                    _destFolder = args[1];
                    if (!Directory.Exists(_destFolder))
                        Directory.CreateDirectory(_destFolder);
                }
                if (_destFolder == null)
                    _destFolder = _sourceFolder;
                if (args.Length == 3)
                    if (args[2].Equals("-keepDuplicates",StringComparison.CurrentCultureIgnoreCase))
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
                    var fileMoved = false;
                    var fileInfo = new FileInfo(fileSystemEntry);
                    if (_jpegExtensions.Contains(fileInfo.Extension.ToLower()))
                    {
                        DateTime dateTime, dateTimeOriginal;
                        string model;

                        try
                        {
                            using (var reader = new ExifReader(fileSystemEntry))
                            {
                                var dateTimeFound = reader.GetTagValue(ExifTags.DateTime, out dateTime);
                                var originalDateTimeFound = reader.GetTagValue(ExifTags.DateTimeOriginal, out dateTimeOriginal);
                                if (originalDateTimeFound)
                                {
                                    dateTime = dateTimeOriginal;
                                }
                                else
                                {
                                    if (!dateTimeFound)
                                    {
                                        Console.WriteLine("Skipping " + fileSystemEntry + " - No DateTimeOriginal or DateTime Exif tag");
                                        continue;
                                    }
                                }
                                if (!reader.GetTagValue(ExifTags.Model, out model))
                                {
                                    Console.WriteLine("Skipping " + fileSystemEntry + " - No Model Exif tag");
                                    continue;
                                }
                            }
                        }
                        catch
                        {
                            // skip pictures where we can't read Exif tags
                            Console.WriteLine("Skipping " + fileSystemEntry + " - No Exif tags");
                            continue;
                        }
                        var yearFolder = dateTime.Year.ToString();
                        var dateFolder = dateTime.ToString("yyyy-MM-dd");
                        var newFolder = Path.Combine(_destFolder, yearFolder, dateFolder, model);

                        Directory.CreateDirectory(newFolder);
                        var newPath = Path.Combine(newFolder, Path.GetFileName(fileSystemEntry));

                        // skip files already in the right place to allow reprocessing directories
                        if (newPath.Equals(fileSystemEntry))
                        {
                            //Console.WriteLine("Skipping " + fileSystemEntry + " - already in correct directory");
                            continue;
                        }

                        if (!File.Exists(newPath))
                        {
                            Console.WriteLine("Moving " + fileSystemEntry + " to " + newPath);
                            File.Move(fileSystemEntry, newPath);
                            fileMoved = true;
                        }
                        else  // handle duplicates
                        {
                            if (_keepDuplicates)
                            {
                                var copyCount = 1;

                                do
                                {
                                    var newFileName = string.Format("{0} ({1}){2}", fileInfo.Name, copyCount++, Path.GetExtension(fileSystemEntry));
                                    newFolder = Path.Combine(new string[] { _destFolder, yearFolder, dateFolder, model, "duplicates" });
                                    newPath = Path.Combine(newFolder, newFileName);
                                }
                                while (File.Exists(newPath));
                                Directory.CreateDirectory(newFolder);
                                Console.WriteLine("Moving " + fileSystemEntry + " to " + newPath);
                                File.Move(fileSystemEntry, newPath);
                                fileMoved = true;
                            }
                            else
                            {
                                //  check filesize and checksum
                                var oldFileInfo = new FileInfo(fileSystemEntry);
                                var newFileInfo = new FileInfo(newPath);

                                // create duplicate only if filesize and checksum don't match
                                if (oldFileInfo.Length != newFileInfo.Length)
                                {

                                    if (!FilesAreEqual(oldFileInfo, newFileInfo))
                                    {
                                        var copyCount = 1;

                                        do
                                        {
                                            var newFileName = string.Format("{0} ({1}){2}", fileInfo.Name, copyCount++, Path.GetExtension(fileSystemEntry));
                                            newFolder = Path.Combine(new string[] { _destFolder, yearFolder, dateFolder, model });
                                            newPath = Path.Combine(newFolder, newFileName);
                                        }
                                        while (File.Exists(newPath));
                                        Directory.CreateDirectory(newFolder);
                                        Console.WriteLine("Moving " + fileSystemEntry + " to " + newPath);
                                        File.Move(fileSystemEntry, newPath);
                                        fileMoved = true;
                                    }
                                }
                                if (!fileMoved)
                                {
                                    Console.WriteLine("Deleting duplicate " + fileSystemEntry);
                                    File.Delete(fileSystemEntry);
                                }
                            }
                        }

                        if (fileMoved)
                        {
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

        static bool FilesAreEqual(FileInfo first, FileInfo second)
        {
            using (var firstFile = first.OpenRead())
            {
                using (var secondFile = second.OpenRead())
                {
                    using (var md5 = MD5.Create())
                    {
                        byte[] firstHash = md5.ComputeHash(firstFile);
                        byte[] secondHash = md5.ComputeHash(secondFile);

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
