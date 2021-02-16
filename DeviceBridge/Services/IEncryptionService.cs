// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Threading.Tasks;
using NLog;

namespace DeviceBridge.Services
{
    public interface IEncryptionService
    {
        Task<string> Decrypt(Logger logger, string encryptedStringWithVersion);

        Task<string> Encrypt(Logger logger, string unencryptedString);
    }
}