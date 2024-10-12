using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PhadiaBackgroundService.Abstracts;

public interface IFileProcessor
{
    Task<string> ReadLargeFileAsync(string filePath);
}
