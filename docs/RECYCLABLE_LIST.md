RecyclableList<T> : IList<T> - Summary

What it is 
- A drop-in replacement for List<T> that implements IList<T>, backed by ArrayPool<T> instead of heap-allocated arrays
- Also implements IDisposable - must be used with using to realize its benefits

The problem it solves
- List<T> doubles its internal array whenever capacity is exceeded, leaving dead arrays for the GC to collect
- Under high throughput (e.g. GetFeed API, StatusData streams), this creates massive array churn. Especially for new allocations.
- Very large arrays land in the LOH, which is never auto-compacted, causing lasting heap fragmentation

How it works
- On capacity growth: rents a larger array from ArrayPool<T>.Shared instead of allocating a new one, then returns the old array to the pool
- On Dispose(): returns the current inner array to the pool; only the small list shell is left for GC
- If never disposed ("abandoned"), the inner array goes to GC like a normal List<T> - ne benefit gained
- In DEBUG/test builds, an Abandoned event fires and fails unit tests if an instance is leaked

Key API surface
- Constructors: empty, with capacity, or from IEnumerable<T>
- All standard IList<T> methods: Add, AddRange, AddRangeAsync, Remove, Sort, BinarySearch, AsSpan(), etc.
- RecyclableListExtension: AsRecyclableList(), AsList(), AsIList(), As EnumerableRegisteredToDispose()

Three canonical use cases
1. Temporary method-scope collection - wrap in using, enumerate/process, let it dispose at end of scope
2. API result memory buffer - buffer DB query results to release DB connections sooner; register disposal with context.Response.RegisterForDispose(request) so it lives through the full request lifecycle (used in GetFeed API)
3. Returned IEnumerable<T> - call .AsEnumerableRegisteredToDispose() so the caller owns lifetime; the instance auto-disposes once enumeration completes

Rules of thumb
- always using - an undisposed instance is no better than List<T>
- Pre-set capacity when count is known to avoid mid-flight rentals
- Not thread-safe; use concurrent primitives if sharing across threads
- Use AsList() (not LINQ .ToList()) when you need a List<T> - it avoids an extra copy for List<T> inputs