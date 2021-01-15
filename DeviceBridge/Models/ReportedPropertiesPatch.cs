// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DeviceBridge.Models
{
    public class ReportedPropertiesPatch
    {
        [Required]
        public IDictionary<string, object> Patch { get; set; }
    }
}