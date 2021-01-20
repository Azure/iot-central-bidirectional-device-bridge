// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using DeviceBridge.Models;
using DeviceBridge.Services;
using Microsoft.QualityTools.Testing.Fakes;
using Moq;
using NLog;
using NUnit.Framework;

namespace DeviceBridge.Providers.Tests
{
    [TestFixture]
    public class StorageProviderTests
    {
        private Mock<IEncryptionService> _encriptionServiceMock;
        private StorageProvider _storageProvider;

        [SetUp]
        public async Task Setup()
        {
            _encriptionServiceMock = new Mock<IEncryptionService>();
            _encriptionServiceMock.Setup(p => p.Encrypt(It.IsAny<Logger>(), It.IsAny<string>())).Returns((Logger _, string s) => Task.FromResult(s));
            _encriptionServiceMock.Setup(p => p.Decrypt(It.IsAny<Logger>(), It.IsAny<string>())).Returns((Logger _, string s) => Task.FromResult(s));
            _storageProvider = new StorageProvider("", _encriptionServiceMock.Object);
        }

        [Test]
        public async Task ListAllSubscriptionsOrderedByDeviceId()
        {
            using (ShimsContext.Create())
            {
                _encriptionServiceMock.Invocations.Clear();
                var testDateTime = DateTime.Now;

                // Return 55 subscriptions.
                var allItems = Enumerable.Repeat(
                    new
                    {
                        DeviceId = "test-device",
                        SubscriptionType = "DesiredProperties",
                        CallbackUrl = "http://test",
                        CreatedAt = testDateTime,
                    }, 55).ToList();

                dynamic currentPage = null;
                dynamic currentItem = null;

                System.Data.SqlClient.Fakes.ShimSqlConnection.AllInstances.OpenAsyncCancellationToken = (_, __) => Task.CompletedTask;

                // Get the next page of 10 items when ExecuteReaderAsync is called.
                System.Data.SqlClient.Fakes.ShimSqlCommand.AllInstances.ExecuteReaderAsync = cmd =>
                {
                    Assert.AreEqual(cmd.CommandText, "getDeviceSubscriptionsPaged");
                    Assert.AreEqual(cmd.CommandType, CommandType.StoredProcedure);

                    var nextPageSize = allItems.Count < 10 ? allItems.Count : 10;
                    currentPage = allItems.Take(nextPageSize).ToList();
                    allItems.RemoveRange(0, nextPageSize);
                    return Task.FromResult<SqlDataReader>(new System.Data.SqlClient.Fakes.ShimSqlDataReader());
                };

                // Get the next item when ReadAsync is called.
                System.Data.SqlClient.Fakes.ShimSqlDataReader.AllInstances.ReadAsyncCancellationToken = (_, __) =>
                {
                    if (currentPage.Count > 0)
                    {
                        currentItem = currentPage[0];
                        currentPage.RemoveAt(0);
                        return Task.FromResult(true);
                    }
                    else
                    {
                        return Task.FromResult(false);
                    }
                };

                System.Data.SqlClient.Fakes.ShimSqlDataReader.AllInstances.ItemGetString = (_, name) =>
                {
                    switch (name)
                    {
                        case "DeviceId":
                            return currentItem.DeviceId;
                        case "SubscriptionType":
                            return currentItem.SubscriptionType;
                        case "CallbackUrl":
                            return currentItem.CallbackUrl;
                        case "CreatedAt":
                            return currentItem.CreatedAt;
                    }

                    return null;
                };

                var result = await _storageProvider.ListAllSubscriptionsOrderedByDeviceId(LogManager.GetCurrentClassLogger());
                Assert.AreEqual(result.FindAll(s => s.DeviceId == "test-device" && s.CallbackUrl == "http://test" && s.SubscriptionType == DeviceSubscriptionType.DesiredProperties && s.CreatedAt == testDateTime).Count, 55);
                _encriptionServiceMock.Verify(p => p.Decrypt(It.IsAny<Logger>(), It.IsAny<string>()), Times.Exactly(55));
            }
        }
    }
}