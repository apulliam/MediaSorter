using System;
using System.IO;
using System.Linq;
using System.CommandLine;
using System.CommandLine.Parsing;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using MetadataExtractor;
using System.CommandLine.IO;
using System.CommandLine.Help;

namespace MediaSorter
{
    internal class MediaSorter
    {
        private static readonly string[] supportedPhotoExtensions = {
            ".jpg", ".jpeg", ".tif", ".tiff",".cr2"
        };

        private static readonly string[] supportedVideoExtensions = {
            ".mov", ".mp4",".mpg"
        };

        private bool _testSort = false; // Write operations to log.  Don't actually move or delete file. 
        private bool _processPhotos = true;
        private bool _processVideos = true;
        private bool _cameraName = false;
        private bool _metadataOnly = false; // Skip photos that don't have date in JPG metadata
        private int _folderCount = 0;
        private int _fileCount = 0;
        private string _sourceFolder = null;
        private string _destFolder = null;
        private bool _keepDuplicates = false;
        private StreamWriter _logFile;

        private static readonly string _duplicatesFolder = "ms-duplicates";

        static void Main(string[] args)
        {
            // Create a root command with some options
            var rootCommand = new RootCommand
            {
                new Option<string>(
                    new string[] { "--sourceFolder", "-s" },
                    "Source folder to be processed.")
                    {
                        IsRequired = true
                    },
                new Option<string>(
                    new string[] { "--destinationFolder", "-d" },
                    "Destination folder for photos.")// (defaults to sourceFolder if not specified).")
                {
                    IsRequired = true
                },
                new Option<bool>(
                    new string[]{"--photos","-p" },
                     getDefaultValue: () => false,
                    "Sort photos (photos and/or videos option required)."),
                
                new Option<bool>(
                    new string[]{"--videos","-v" },
                    getDefaultValue: () => false,
                    "Sort videos (photos and/or videos option required)."),
                
                new Option<bool>(
                    new string[]{"--testSort","-t" },
                    getDefaultValue: () => false,
                    "Test sort operation.  Write intended move and delete operations to log file only."),

                new Option<bool>(
                    new string[]{"--metadataOnly","-m" },
                    getDefaultValue: () => false,
                    "Sort photos only by date in metadata.  Skip files if date not found in Exif metadata. Default is to fallback to file modified date."),
                
                new Option<bool>(
                    new string[]{"--useCameraName","-c" },
                    getDefaultValue: () => false,
                    "Sort photos into camera name folder under date folder.  Useful to differentiate photos on same date from more than one one camera. Only applies to photos."),

                new Option<bool>(
                    new string[]{"--keepDuplicates","-k" },
                    getDefaultValue: () => false,
                    "Keep duplicates in ms-duplicates folder."),
                
                new Option<bool>(
                    new string[]{"--help","-h" },
                    getDefaultValue: () => false,
                    "Shows this usage message.")
               
               
            };

            rootCommand.Description = "MediaSorter sorts photos and videos into folders by year and date.  Photos can optionally be sorted by into camera name folder under date folder.";

            var parseResult = rootCommand.Parse(args);
            
            var help = parseResult.ValueForOption<bool>("--help");
            if (!help && parseResult.Errors.Count == 0)
            {
                var sourceFolder = parseResult.ValueForOption<string>("--sourceFolder");
                var destinationFolder = parseResult.ValueForOption<string>("--destinationFolder");
                var processPhotos = parseResult.ValueForOption<bool>("--photos");
                var processVideos = parseResult.ValueForOption<bool>("--videos");
                var testSort = parseResult.ValueForOption<bool>("--testSort");
                var exifOnly = parseResult.ValueForOption<bool>("--metadataOnly");
                var useCameraName = parseResult.ValueForOption<bool>("--useCameraName");
                var keepDuplicates = parseResult.ValueForOption<bool>("--keepDuplicates");
                if (processPhotos || processVideos)
                {

                    if (!processPhotos && (exifOnly || useCameraName))
                    {
                        Console.WriteLine("Warning: useCameraName and exifOnly options only work with photos.\n");
                    }

                    var mediaSorter = new MediaSorter(sourceFolder,
                                          destinationFolder,
                                          processPhotos,
                                          processVideos,
                                          testSort,
                                          exifOnly,
                                          useCameraName,
                                          keepDuplicates);
                    mediaSorter.SortMedia();
                    return;
                }
                else
                {
                    Console.WriteLine("MediaSorter requires either the photos and/or videos option.\n");
                }

            }

            // Fall through and print usage message

            var systemConsole = new SystemConsole();
            var helpBuilder = new HelpBuilder(systemConsole);
            helpBuilder.Write(rootCommand);
        }

        internal MediaSorter(string sourceFolder,
                                string destinationFolder,
                                bool processPhotos,
                                bool processVideos,
                                bool testSort,
                                bool metadataOnly,
                                bool cameraName,
                                bool keepDuplicates)
        {
            _sourceFolder = sourceFolder;
            _destFolder = destinationFolder;
            _processPhotos = processPhotos;
            _processVideos = processVideos;
            _testSort = testSort;
            _metadataOnly = metadataOnly;
            _cameraName = cameraName;
            _keepDuplicates = keepDuplicates;
        }

        internal void SortMedia()
        {
            var startTime = DateTime.Now;
            var logFileName = $"MediaSorter-{startTime:yyyy-MM-dd_HH-mm-ss-tt}.log";
            _logFile = new StreamWriter(logFileName);

            try
            { 
                if (!System.IO.Directory.Exists(_sourceFolder))
                    throw new ArgumentException($"SourceFolder {_sourceFolder} not found");

                if (!System.IO.Directory.Exists(_destFolder))
                    System.IO.Directory.CreateDirectory(_destFolder);

                ProcessFolder(_sourceFolder);

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                _logFile.Write(ex);
            }
            finally
            {
                var elapsedTime = DateTime.Now - startTime;
                var completedMessage = $"Processed {_fileCount} files in {_folderCount} folders, elapsed time = {elapsedTime}";
                _logFile.WriteLine(completedMessage);
                Console.WriteLine(completedMessage);
                Console.WriteLine($"Details in log file {logFileName}");
                _logFile?.Dispose();
            }
        }

        private bool IsCleanup(FileInfo fileInfo)
        {
            // delete Thumbs.db 
            if (_processPhotos && (fileInfo.Name.Equals("Thumbs.db") || fileInfo.Name.Equals("ZbThumbnail.info")))
            {

                FileDelete(fileInfo.FullName);
                return true;
            }
            if (_processVideos && (fileInfo.Extension.ToLower().Equals(".thm") || fileInfo.Name.Equals("ZbThumbnail.info")))
            {
                FileDelete(fileInfo.FullName);
                return true;
            }

            return false;
        }

        private static bool IsPhotoFile(FileInfo fileInfo)
        {
            return supportedPhotoExtensions.Contains(fileInfo.Extension.ToLower());
        }

        private static bool IsVideoFile(FileInfo fileInfo)
        {
            return supportedVideoExtensions.Contains(fileInfo.Extension.ToLower());
        }


        private static string GetTargetFolderForFile(FileInfo fileInfo)
        {
            var dateTime = fileInfo.LastWriteTime;
            var year = dateTime.Year.ToString();
            var date = dateTime.ToString("yyyy-MM-dd");
            return Path.Combine(year, date);
        }

        private static string GetTargetFolderForVideo(FileInfo fileInfo)
        {
            var metadataDirectories = ImageMetadataReader.ReadMetadata(fileInfo.FullName);
            
            var quickTimeMovieHeaderDirectory = metadataDirectories.OfType<MetadataExtractor.Formats.QuickTime.QuickTimeMovieHeaderDirectory>().FirstOrDefault();
            if (quickTimeMovieHeaderDirectory == null)
                return null;
           
            if (!quickTimeMovieHeaderDirectory.TryGetDateTime(QuickTimeMovieHeaderDirectory.TagCreated, out DateTime dateTime))
            {
               return null;
            }
              
            var year = dateTime.Year.ToString();
            var date = dateTime.ToString("yyyy-MM-dd");
            return Path.Combine(year, date);
        }

        private string GetTargetFolderForPhoto(FileInfo fileInfo)
        {
            var metadataDirectories = ImageMetadataReader.ReadMetadata(fileInfo.FullName);

            var subIfdDirectory = metadataDirectories?.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (subIfdDirectory == null)
                return null;

            if (!subIfdDirectory.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out DateTime dateTime))
            {
                return null;
            }

            var year = dateTime.Year.ToString();
            var date = dateTime.ToString("yyyy-MM-dd");

            if (_cameraName)
            {
                var id0Directory = metadataDirectories.OfType<ExifIfd0Directory>().FirstOrDefault();
           
                var model = id0Directory?.GetDescription(ExifDirectoryBase.TagModel);
                var make = id0Directory?.GetDescription(ExifDirectoryBase.TagMake);
                if (model != null && make != null)
                {
                    string camera;
                    if (model.StartsWith(make))
                        camera = model.Trim();
                    else
                        camera = $"{make?.Trim()} {model?.Trim()}";
                    return Path.Combine(year, date, camera);
                }
            }
            
            return Path.Combine(year, date);
        }

        private void ProcessFolder(string folder)
        {
            // Recursively process directories
            foreach (var directory in System.IO.Directory.GetDirectories(folder))
            {
                ++_folderCount;
                var directoryInfo = new DirectoryInfo(directory);

                // ignore "duplicates" directory so that same directories can be reprocessed
                if (!directoryInfo.Name.Equals(_duplicatesFolder, StringComparison.InvariantCultureIgnoreCase))
                    ProcessFolder(directory);
            }

            foreach (var filePath in System.IO.Directory.GetFiles(folder))
            {
                ++_fileCount;
                var fileInfo = new FileInfo(filePath);
                ProcessFile(fileInfo);
            }

            // Delete empty source directories
            if (!System.IO.Directory.EnumerateFileSystemEntries(folder).Any())
            {

                DirectoryDelete(folder);
            }
        }

        private void ProcessFile(FileInfo fileInfo)
        {
            // delete thumbnails
            if (IsCleanup(fileInfo))
            {
                return;
            }
            
            string targetFolder;
            try
            {
                if (_processPhotos && IsPhotoFile(fileInfo))
                {
                    targetFolder = GetTargetFolderForPhoto(fileInfo);

                }
                else if (_processVideos && IsVideoFile(fileInfo))
                {
                    targetFolder = GetTargetFolderForVideo(fileInfo);
                }
                else
                {
                    _logFile.WriteLine($"Skipping unsupported file: {fileInfo.FullName}");
                    return;
                }
            }
            catch (ImageProcessingException ex)
            {
                _logFile.WriteLine($"Skipping file due to image processing exception ({ex.Message}): {fileInfo.FullName}");
                return;
            }

            if (!_metadataOnly && targetFolder == null)
                targetFolder = GetTargetFolderForFile(fileInfo);

            if (targetFolder == null)
            {
                _logFile.WriteLine($"Could not generate target folder for {fileInfo.FullName}");
                return;
            }
        
            var targetPath = Path.Combine(_destFolder, targetFolder, fileInfo.Name);

            // check if file is already in correct place
            if (targetPath.Equals(fileInfo.FullName))
            {
                // skip files already in the right place to allow reprocessing directories
                _logFile.WriteLine($"Skipping {fileInfo.FullName} - already in correct directory");
                return;
            }

            // new path and existing path are different, now check to see if new path exists
            if (File.Exists(targetPath)) // handle duplicates
            {

                var newFileInfo = new FileInfo(targetPath);

                if (!fileInfo.FilesAreEqual(newFileInfo))  // always move file to destination directory as copy if the size and checksum are different
                {
                    targetPath = fileInfo.CreateDuplicateFileName(targetFolder);
                    FileMove(fileInfo.FullName, targetPath);
                }
                else if (_keepDuplicates) // for true duplicates only keep if flag set
                {
                    targetFolder = Path.Combine(targetFolder, _duplicatesFolder);
                    System.IO.Directory.CreateDirectory(targetFolder);
                    targetPath = fileInfo.CreateDuplicateFileName(targetFolder);
                    FileMove(fileInfo.FullName, targetPath);
                }
                else // default is to delete duplicates
                {
                    _logFile.WriteLine($"Detected duplicate {fileInfo.FullName}");
                    FileDelete(fileInfo.FullName);
                }
            }
            else
            {
                var targetDirectory = Path.GetDirectoryName(targetPath);
                System.IO.Directory.CreateDirectory(targetDirectory);
                FileMove(fileInfo.FullName, targetPath);
            }
        }

        private void FileMove(string sourceFileName, string destFileName)
        {
            _logFile.WriteLine($"Moving {sourceFileName} to {destFileName}");
            if (!_testSort)
                File.Move(sourceFileName, destFileName);
        }

        private void FileDelete(string path)
        {
            _logFile.WriteLine($"Deleting file {path}");
            if (!_testSort)
                File.Delete(path);
        }

        private void DirectoryDelete(string path)
        {
            _logFile.WriteLine($"Removing directory {path}");
            if (!_testSort)
                System.IO.Directory.Delete(path);
        }
    }
}
