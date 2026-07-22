using System;
using System.Collections.Generic;
using OttoMapper.Mapping.Generated;

namespace OttoMapper.Generator.Tests;

// ---- Simple convention map with a numeric conversion (int -> long) ----
public class SrcSimple
{
    public string? Name { get; set; }
    public int Age { get; set; }
    public Guid Id { get; set; }
    public string? ExtraSourceOnly { get; set; }
}

[AutoMap(typeof(SrcSimple))]
public class DstSimple
{
    public string? Name { get; set; }
    public long Age { get; set; }
    public Guid Id { get; set; }
    public string? DestOnly { get; set; } // no matching source -> left at default
}

// ---- Enum -> enum conversion ----
public enum SrcEnum { A, B, C }
public enum DstEnum { X, Y, Z }

public class SrcWithEnum
{
    public SrcEnum Value { get; set; }
}

[AutoMap(typeof(SrcWithEnum))]
public class DstWithEnum
{
    public DstEnum Value { get; set; }
}

// ---- Nested object + collection of nested objects (depends on DstChild being generated) ----
public class SrcChild
{
    public string? Name { get; set; }
}

[AutoMap(typeof(SrcChild))]
public class DstChild
{
    public string? Name { get; set; }
}

public class SrcParent
{
    public SrcChild? Child { get; set; }
    public List<SrcChild> Children { get; set; } = new();
}

[AutoMap(typeof(SrcParent))]
public class DstParent
{
    public DstChild? Child { get; set; }
    public List<DstChild> Children { get; set; } = new();
}

// ---- Collection element conversion (int -> long) ----
public class SrcNumbers
{
    public List<int> Values { get; set; } = new();
}

[AutoMap(typeof(SrcNumbers))]
public class DstNumbers
{
    public List<long> Values { get; set; } = new();
}

// ---- Array destination ----
public class SrcArray
{
    public List<int> Items { get; set; } = new();
}

[AutoMap(typeof(SrcArray))]
public class DstArray
{
    public int[] Items { get; set; } = Array.Empty<int>();
}

// ---- Per-property attributes: MapSource + IgnoreMap ----
public class SrcOverride
{
    public string? DisplayName { get; set; }
    public string? ShouldSkip { get; set; }
}

[AutoMap(typeof(SrcOverride))]
public class DstOverride
{
    [MapSource("DisplayName")]
    public string? Name { get; set; }

    [IgnoreMap]
    public string? Skip { get; set; }
}

// ---- Underscore + case-insensitive matching ----
public class SrcNaming
{
    public string? EMails_ID { get; set; }
}

[AutoMap(typeof(SrcNaming))]
public class DstNaming
{
    public string? EmailsId { get; set; }
}