using System.IO;
using System.Security.Cryptography;

namespace MediaSorter
{
    static class FileInfoExtensions
    {
        public static string CreateDuplicateFileName(this FileInfo fileInfo, string folder)
        {
            var copyCount = 1;
            string newPath;
            do
            {
                var newFileName = $"{fileInfo.Name}({copyCount++}){fileInfo.Extension}";
                newPath = Path.Combine(folder, newFileName);
            }
            while (File.Exists(newPath));
            return newPath;
        }


        public static bool FilesAreEqual(this FileInfo first, FileInfo second)
        {
            //  if filesize if different, files are different
            if (first.Length != second.Length)
                return true;
            // checksum is expensive, so on check if filesize is different
            // log this case since it should be rare
            //_logFile.WriteLine($"{first.FullName} and {second.FullName} have same length. Doing checksum comparison...");
            using var firstFile = first.OpenRead();
            using var secondFile = second.OpenRead();
            using var md5 = MD5.Create();
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
