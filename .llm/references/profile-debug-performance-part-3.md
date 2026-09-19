# profile-debug-performance - Part 3

## Split Content

## Automated Performance Regression Tests

### Performance Test Framework

```csharp
using NUnit.Framework;
using Unity.PerformanceTesting;

public class PerformanceTests
{
    [Test, Performance]
    public void MeasureMethodPerformance()
    {
        Measure.Method(() =>
        {
            MyExpensiveMethod();
        })
        .WarmupCount(10)
        .MeasurementCount(100)
        .Run();
    }
}
```

### CI Performance Gates

```csharp
[Test]
public void ProcessItems_UnderBudget()
{
    Stopwatch sw = Stopwatch.StartNew();

    for (int i = 0; i < 1000; i++)
    {
        _processor.Process(_testItems);
    }

    sw.Stop();
    double msPerCall = sw.ElapsedMilliseconds / 1000.0;

    Assert.Less(msPerCall, 0.5, $"Process took {msPerCall}ms, budget is 0.5ms");
}
```

---

## Optimization Checklist

### Before Optimizing

- [ ] Have you profiled on target platform?
- [ ] Is this actually a hot path?
- [ ] What's the current allocation/time cost?
- [ ] What's the acceptable budget?

### After Optimizing

- [ ] Did you verify improvement with profiler?
- [ ] Did you add regression tests?
- [ ] Did you document the optimization reason?
- [ ] Is the code still maintainable?

---

## Related Skills

- [high-performance-csharp](../skills/high-performance-csharp.md) — Performance patterns
- [unity-performance-patterns](../skills/unity-performance-patterns.md) — Unity-specific patterns
- [performance-audit](../skills/performance-audit.md) — Code review checklist
- [refactor-to-zero-alloc](../skills/refactor-to-zero-alloc.md) — Migration patterns
