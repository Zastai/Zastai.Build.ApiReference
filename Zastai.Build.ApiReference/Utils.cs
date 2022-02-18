using JetBrains.Annotations;

namespace Zastai.Build.ApiReference;

/// <summary>General utilities.</summary>
[PublicAPI]
internal static class Utils {

  public static ulong ToULong(this object value) => value is ulong u64 ? u64 : (ulong) Convert.ToInt64(value);


}
