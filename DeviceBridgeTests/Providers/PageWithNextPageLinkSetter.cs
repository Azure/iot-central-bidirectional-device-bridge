// Copyright (c) Microsoft Corporation. All rights reserved.

using System.Collections;
using System.Collections.Generic;
using Microsoft.Rest.Azure;
using Newtonsoft.Json;

[JsonObject]
public class PageWithNextPageLinkSetter<T> : IPage<T>, IEnumerable<T>, IEnumerable
{
    private IEnumerator<T> initialList;

    public PageWithNextPageLinkSetter(IEnumerator<T> initialList)
    {
        this.initialList = initialList;
    }

    [JsonProperty("nextLink")]
    public string NextPageLink { get; set; }

    public IEnumerator<T> GetEnumerator()
    {
        return initialList;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return initialList;
    }
}