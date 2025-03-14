﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.Diagnostics.Telemetry;
using Microsoft.Diagnostics.Telemetry.Internal;
using System;
using System.Diagnostics.Tracing;

namespace AIDevGallery.Telemetry.Events;

[EventData]
internal class SearchModelEvent : EventBase
{
    public override PartA_PrivTags PartA_PrivTags => PrivTags.ProductAndServiceUsage;

    public string Query { get; }

    private SearchModelEvent(string query)
    {
        Query = query;
    }

    public override void ReplaceSensitiveStrings(Func<string?, string?> replaceSensitiveStrings)
    {
        // No sensitive strings to replace.
    }

    public static void Log(string query)
    {
        TelemetryFactory.Get<ITelemetry>().Log("SearchModel_Event", LogLevel.Critical, new SearchModelEvent(query));
    }
}