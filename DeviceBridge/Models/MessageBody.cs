// Copyright (c) Microsoft Corporation. All rights reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DeviceBridge.Models
{
    public class MessageBody
    {
        [Required]
        public IDictionary<string, object> Data { get; set; }

        public IDictionary<string, string> Properties { get; set; }

        public string ComponentName { get; set; }

        public DateTime? CreationTimeUtc { get; set; }
    }
}