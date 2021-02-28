using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using Microsoft.Extensions.Primitives;
using System.Collections.Concurrent;

namespace FunctionApp1
{
    public static class DeviceBridgeE2EEcho
    {
        static ConcurrentDictionary<string, string> cache = new ConcurrentDictionary<string, string>();

        // Temporary storage for Device Bridge E2E testing.
        // deviceId must be passed as a query param.
        // POST will store the body for a given device ID in memory.
        // GET will retrieve the last body posted for a given device ID.
        // DELETE will remove the information from the in memory storage for a given device ID.
        [FunctionName("DeviceBridgeE2EEcho")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", "delete", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Called");
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            StringValues deviceIds;
            req.Query.TryGetValue("deviceId", out deviceIds);
            string deviceId = deviceIds.ToArray()[0];

            if (req.Method == "GET")
            {
                string outValue;
                cache.TryGetValue(deviceId, out outValue);
                return new OkObjectResult(outValue);
            } else if (req.Method == "POST")
            {
                if (cache.ContainsKey(deviceId))
                {
                    cache.TryRemove(deviceId, _);
                    if (!cache.TryRemove(deviceId, _))
                    {
                        return new ConflictObjectResult("Conflict when adding to dictionary");
                    }
                }
                if(!cache.TryAdd(deviceId, requestBody))
                {
                    return new ConflictObjectResult("Conflict when adding to dictionary");
                }
            } else if(req.Method == "DELETE")
            {
                if(!cache.TryRemove(deviceId, _))
                {
                    return new ConflictObjectResult("Conflict when deleting from dictionary");
                }
            }

            return new OkObjectResult("Undefined");
        }
    }
}
