// Copyright (c) Microsoft Corporation. All rights reserved.

using System.ComponentModel.DataAnnotations;

namespace DeviceBridge.Models
{
    public class SubscriptionCreateOrUpdateBody
    {
        [Required]
        public string CallbackUrl { get; set; }
    }
}