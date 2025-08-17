# WorkItem GetField Type Conversion Fix

## Issue Summary

The `WorkItem.GetField<T>` method was throwing `InvalidCastException` when trying to cast Azure DevOps field values from strings to numeric types:

```
System.InvalidCastException: 'Unable to cast object of type 'System.String' to type 'System.Nullable`1[System.Double]'.'
```

## Root Cause

Azure DevOps API often returns numeric values as strings in JSON responses, but the original `GetField<T>` method performed a direct cast `(T)fieldValue` which fails when the stored type doesn't match the requested type exactly.

## Solution

Enhanced the `GetField<T>` methods with intelligent type conversion by implementing a `ConvertFieldValue<T>` method that handles:

### 1. **String to Numeric Conversions** (Primary Issue)
```csharp
// Handles Azure DevOps scenarios where "8.5" string needs to become double? 8.5
if (sourceType == typeof(string) && fieldValue is string stringValue)
{
    if (targetType == typeof(double))
        return (T)(object)double.Parse(stringValue);
    // ... other numeric types
}
```

### 2. **Nullable Type Support**
```csharp
// Handles nullable types like double?, int?, etc.
var underlyingType = Nullable.GetUnderlyingType(targetType);
if (underlyingType != null)
{
    targetType = underlyingType; // Work with underlying type
}
```

### 3. **Robust Error Handling**
```csharp
try
{
    // Type conversion logic
}
catch (Exception)
{
    // Fallback to direct cast, then default value
    return default(T);
}
```

## Types Supported

### **Numeric Types**
- `double`, `double?` - Common for CompletedWork, Effort fields
- `float`, `float?` 
- `int`, `int?` - Common for IDs, revision numbers
- `long`, `long?`
- `decimal`, `decimal?`

### **Other Types**
- `bool`, `bool?` - Supports "true"/"false" strings
- `DateTime`, `DateTime?` - Supports date strings
- `string` - Converts any type to string via ToString()

### **Direct Type Matches**
- When source and target types match exactly, returns as-is for performance

## Hierarchical Aggregation Script Compatibility

The fix specifically resolves issues in the hierarchical aggregation script calls like:

```csharp
// These calls now work when fields are stored as strings
var completedWork = task.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork") ?? 0;
var effort = feature.GetField<double?>("Microsoft.VSTS.Scheduling.Effort") ?? 0;
var devEstimation = feature.GetField<double?>("Custom.DevelopmentEffortEstimation") ?? 0;
```

## Test Coverage

Created comprehensive tests verifying:

? **String to Double Conversion**: `"12.5"` ? `12.5`  
? **String to Int Conversion**: `"123"` ? `123`  
? **Nullable Type Handling**: `null` ? `null`, `"8.5"` ? `8.5`  
? **Direct Type Matches**: `15.75` ? `15.75` (no conversion needed)  
? **Empty String Handling**: `""` ? `null` for numeric types  
? **Default Value Support**: Missing fields return specified defaults  
? **Numeric Type Conversions**: `int` ? `double` conversions  
? **Boolean Conversions**: `"true"` ? `true`  
? **Invalid Conversion Graceful Handling**: `"not-a-number"` ? `null`  
? **Hierarchical Aggregation Scenarios**: Exact script usage patterns  

## Performance Considerations

- **Fast Path**: Direct type matches return immediately without conversion
- **Minimal Overhead**: Type checking and conversion only when needed
- **Graceful Degradation**: Falls back to direct cast, then default value
- **Memory Efficient**: No intermediate allocations for direct matches

## Backward Compatibility

? **100% Backward Compatible**: Existing code continues to work unchanged  
? **Enhanced Functionality**: New type conversion capabilities  
? **Same API**: No breaking changes to method signatures  
? **Existing Tests**: All existing functionality preserved  

## Files Modified

### 1. **`Src\Lamdat.ADOAutomationTool.Entities\WorkItem.cs`**
- Enhanced `GetField<T>(string fieldName)` 
- Enhanced `GetField<T>(string fieldName, T defaultValue)`
- Added `ConvertFieldValue<T>(object? fieldValue)` - intelligent conversion logic
- Added `IsNumericType(Type type)` - helper for numeric type detection

### 2. **`Tests\Lamdat.ADOAutomationTool.Tests\Entities\WorkItemGetFieldTests.cs`** (New)
- Comprehensive test suite with 10 test cases
- Validates all conversion scenarios
- Tests exact hierarchical aggregation script usage patterns

## Production Impact

### **Before Fix:**
? InvalidCastException when Azure DevOps returns numeric fields as strings  
? Hierarchical aggregation script crashes  
? No type conversion support  

### **After Fix:**
? All Azure DevOps field types handled gracefully  
? Hierarchical aggregation script works reliably  
? Intelligent type conversion for common scenarios  
? Robust error handling with fallbacks  
? 100% backward compatibility maintained  

## Azure DevOps Field Examples

Common scenarios now handled automatically:

```csharp
// Azure DevOps stores these as strings, but script needs doubles
workItem.GetField<double?>("Microsoft.VSTS.Scheduling.CompletedWork")     // "8.5" ? 8.5
workItem.GetField<double?>("Microsoft.VSTS.Scheduling.Effort")           // "40" ? 40.0
workItem.GetField<double?>("Microsoft.VSTS.Scheduling.RemainingWork")    // "15.5" ? 15.5
workItem.GetField<int?>("System.Rev")                                    // "5" ? 5
workItem.GetField<bool?>("Custom.IsEnabled")                            // "true" ? true
```

---

## ? **Status: IMPLEMENTED AND TESTED**

The `WorkItem.GetField<T>` method now provides robust type conversion capabilities that handle all common Azure DevOps field scenarios while maintaining 100% backward compatibility. The hierarchical aggregation script and all existing functionality continue to work seamlessly.