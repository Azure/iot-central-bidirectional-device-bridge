// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Collections.Generic;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DeviceBridge.Models
{
    public class DeviceTwin
    {
        public static readonly OpenApiSchema Schema = new OpenApiSchema()
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>()
            {
                {
                    "twin", new OpenApiSchema()
                    {
                        Type = "object",
                        Properties = new Dictionary<string, OpenApiSchema>()
                        {
                            {
                                "properties", new OpenApiSchema()
                                {
                                    Type = "object",
                                    Properties = new Dictionary<string, OpenApiSchema>()
                                    {
                                        {
                                            "desired", new OpenApiSchema()
                                            {
                                                Type = "object",
                                            }
                                        },
                                        {
                                            "reported", new OpenApiSchema()
                                            {
                                                Type = "object",
                                            }
                                        },
                                    },
                                }
                            },
                        },
                    }
                },
            },
        };

        [JsonProperty("twin")]
        public JRaw Twin { get; set; }
    }
}