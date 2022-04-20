using JetBrains.Annotations;

namespace Zastai.Build.ApiReference;

/// <summary>General utilities.</summary>
[PublicAPI]
internal static class Utils {

  public static bool Matches(this string text, string pattern) {
    if (pattern.IndexOfAny(new[] { '*', '?' }) < 0) {
      return text == pattern;
    }
    var textLength = text.Length;
    var textPosition = 0;
    var patternLength = pattern.Length;
    var patternPosition = 0;
    while (true) {
      if ((textPosition == textLength) != (patternPosition == patternLength)) { // either text or pattern is exhausted
        if (textPosition != textLength) {
          return false;
        }
        // If the remaining pattern consists only of '*', it's still a match
        while (patternPosition < patternLength && pattern[patternPosition] == '*') {
          ++patternPosition;
        }
        return patternPosition == patternLength;
      }
      if ((textPosition == textLength) && (patternPosition == patternLength)) { // both exhausted -> match
        return true;
      }
      var p = pattern[patternPosition];
      if (p == '*') {
        while (patternPosition < patternLength && pattern[patternPosition] == '*') {
          ++patternPosition;
        }
        if (patternPosition == patternLength) { // trailing * -> match
          return true;
        }
        var remainingPattern = pattern.Substring(patternPosition);
        for (; textPosition < textLength; ++textPosition) {
          var remainingText = text.Substring(textPosition);
          if (remainingText.Matches(remainingPattern)) {
            return true;
          }
        }
        return false;
      }
      if (p != '?' && text[textPosition] != p) {
        return false;
      }
      ++patternPosition;
      ++textPosition;
    }

  }

  public static ulong ToULong(this object value) => value is ulong u64 ? u64 : (ulong) Convert.ToInt64(value);


}
