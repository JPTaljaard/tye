// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Tye.Hosting
{
    public class ReplicaRegistry : IDisposable
    {
        private readonly string? _instanceName;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, object> _fileWriteLocks;
        private readonly string _tyeFolderPath;

        public ReplicaRegistry(string directory, string? instanceName, ILogger logger)
        {
            _instanceName = instanceName;
            _logger = logger;
            _fileWriteLocks = new ConcurrentDictionary<string, object>();
            _tyeFolderPath = Path.Join(directory, ".tye");
        }

        public bool WriteReplicaEvent(string storeName, IDictionary<string, string> replicaRecord)
        {
            if (!Directory.Exists(_tyeFolderPath))
            {
                Directory.CreateDirectory(_tyeFolderPath);
            }

            var filePath = Path.Join(_tyeFolderPath, GetStoreFile(storeName));
            var contents = JsonSerializer.Serialize(replicaRecord, new JsonSerializerOptions { WriteIndented = false });
            var lockObj = GetLockForStore(storeName);

            lock (lockObj)
            {
                try
                {
                    File.AppendAllText(filePath, contents + Environment.NewLine);
                    return true;
                }
                catch (DirectoryNotFoundException ex)
                {
                    _logger.LogWarning(ex, "tye folder is not found. file: {file}", filePath);
                    return false;
                }
            }
        }

        public bool DeleteStore(string storeName)
        {
            var filePath = Path.Join(_tyeFolderPath, GetStoreFile(storeName));

            if (!File.Exists(filePath))
            {
                return false;
            }

            try
            {
                File.Delete(filePath);
                return true;
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.LogWarning(ex, "tye folder is not found. file: {file}", filePath);
                return false;
            }
        }

        public async ValueTask<IList<IDictionary<string, string>>> GetEvents(string storeName)
        {
            var filePath = Path.Join(_tyeFolderPath, GetStoreFile(storeName));

            if (!File.Exists(filePath))
            {
                return Array.Empty<IDictionary<string, string>>();
            }

            var contents = await File.ReadAllTextAsync(filePath);
            var events = contents.Split(Environment.NewLine);

            return events.Where(e => !string.IsNullOrEmpty(e.Trim()))
                .Select(e => JsonSerializer.Deserialize<IDictionary<string, string>>(e))
                .ToList()!;
        }

        private object GetLockForStore(string storeName)
        {
            var lockName = string.IsNullOrEmpty(_instanceName) ? storeName : $"{_instanceName}_{storeName}";
            return _fileWriteLocks.GetOrAdd(lockName, _ => new object());
        }

        private string GetStoreFile(string storeName) => string.IsNullOrEmpty(_instanceName) ? $"{storeName}_store" : $"{_instanceName}_{storeName}_store";

        public void Dispose()
        {
            if (Directory.Exists(_tyeFolderPath))
            {
                try
                {
                    DirectoryExtensions.DeleteDirectory(_tyeFolderPath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }
    }
}
