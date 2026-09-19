# use-serialization - Part 3

## Split Content

## Common Pitfalls

### Missing ProtoContract

```csharp
// ❌ Will fail - missing attribute
public class MyData
{
    public int Value { get; set; }
}

// ✅ Correct
[ProtoContract]
public class MyData
{
    [ProtoMember(1)]
    public int Value { get; set; }
}
```

### Reusing ProtoMember Numbers

```csharp
// ❌ Data corruption when loading old saves
[ProtoMember(2)]  // Was previously "OldField"
public int NewField { get; set; }

// ✅ Use new number, reserve old
[ProtoReserved(2)]
[ProtoMember(3)]
public int NewField { get; set; }
```

### Circular References

```csharp
// ❌ Stack overflow
public class Node
{
    public Node Parent { get; set; }  // Circular!
}

// ✅ Use AsReference
[ProtoContract]
public class Node
{
    [ProtoMember(1, AsReference = true)]
    public Node Parent { get; set; }
}
```
