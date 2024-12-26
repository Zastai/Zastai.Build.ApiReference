using System.Text.RegularExpressions;

namespace Zastai.Build.ApiReference;

/// <summary>Various constants.</summary>
public static class Constants {

  /// <summary>
  /// A regular expression representing a valid F# custom operator method name (not including the <c>op_</c> prefix or possible
  /// <c>$W</c> suffix).<br/>
  /// This consists of one or more words representing the symbols F# allows in custom operators:
  /// <list type="table">
  ///   <listheader>
  ///     <term>Symbol</term>
  ///     <description>Word</description>
  ///   </listheader>
  ///   <item>
  ///     <term>&amp;</term>
  ///     <description>Amp</description>
  ///   </item>
  ///   <item>
  ///     <term>@</term>
  ///     <description>At</description>
  ///   </item>
  ///   <item>
  ///     <term>!</term>
  ///     <description>Bang</description>
  ///   </item>
  ///   <item>
  ///     <term>|</term>
  ///     <description>Bar</description>
  ///   </item>
  ///   <item>
  ///     <term>,</term>
  ///     <description>Comma</description>
  ///   </item>
  ///   <item>
  ///     <term>/</term>
  ///     <description>Divide</description>
  ///   </item>
  ///   <item>
  ///     <term>$</term>
  ///     <description>Dollar</description>
  ///   </item>
  ///   <item>
  ///     <term>.</term>
  ///     <description>Dot</description>
  ///   </item>
  ///   <item>
  ///     <term>=</term>
  ///     <description>Equals</description>
  ///   </item>
  ///   <item>
  ///     <term>&gt;</term>
  ///     <description>Greater</description>
  ///   </item>
  ///   <item>
  ///     <term>^</term>
  ///     <description>Hat</description>
  ///   </item>
  ///   <item>
  ///     <term>[</term>
  ///     <description>LBrack</description>
  ///   </item>
  ///   <item>
  ///     <term>(</term>
  ///     <description>LParen</description>
  ///   </item>
  ///   <item>
  ///     <term>&lt;</term>
  ///     <description>Less</description>
  ///   </item>
  ///   <item>
  ///     <term>-</term>
  ///     <description>Minus</description>
  ///   </item>
  ///   <item>
  ///     <term>*</term>
  ///     <description>Multiply</description>
  ///   </item>
  ///   <item>
  ///     <term>%</term>
  ///     <description>Percent</description>
  ///   </item>
  ///   <item>
  ///     <term>+</term>
  ///     <description>Plus</description>
  ///   </item>
  ///   <item>
  ///     <term>?</term>
  ///     <description>Qmark</description>
  ///   </item>
  ///   <item>
  ///     <term>]</term>
  ///     <description>RBrack</description>
  ///   </item>
  ///   <item>
  ///     <term>)</term>
  ///     <description>RParen</description>
  ///   </item>
  ///   <item>
  ///     <term>~</term>
  ///     <description>Twiddle</description>
  ///   </item>
  /// </list>
  /// </summary>
  public static readonly Regex FSharpCustomOperatorPattern =
    new("^(?:Amp|At|Bang|Bar|Comma|Divide|Dollar|Dot|Equals|Greater|Hat|LBrack|LParen|Less|Minus|Multiply|Percent|Plus|Qmark|RBrack|RParen|Twiddle)+$");

}
