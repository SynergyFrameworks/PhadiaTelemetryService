using PhadiaBackgroundService.Abstracts;
using System.Text;

namespace PhadiaBackgroundService.Infrastructure
{
    public class FileProcessor : IFileProcessor
    {
        public async Task<string> ReadLargeFileAsync(string filePath)
        {
            const int bufferSize = 4096;
            StringBuilder contentBuilder = new StringBuilder();
            using (var sourceStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var reader = new StreamReader(sourceStream))
            {
                char[] buffer = new char[bufferSize];
                int bytesRead;
                while ((bytesRead = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    contentBuilder.Append(buffer, 0, bytesRead);
                }
            }
            return contentBuilder.ToString();
        }
    }
}