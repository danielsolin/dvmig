using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace dvmig.XTB.Shared
{
   internal static class XTBLogWriter
   {
      private static readonly Color DefaultColor = Color.Gainsboro;
      private static readonly Color SyncedColor = Color.LightGreen;
      private static readonly Color AccentColor = Color.DeepSkyBlue;

      public static void AppendLogMessage(RichTextBox logControl, string msg)
      {
         AppendColoredText(
            logControl,
            $"[{DateTime.Now:HH:mm:ss}] {msg}\n",
            IsSyncedMessage(msg) ? SyncedColor : DefaultColor
         );
      }

      public static void AppendWelcomeBanner(
         RichTextBox logControl,
         params string[] lines
      )
      {
         var width = Math.Max(0, lines.Max(line => line.Length));
         var border = "+" + new string('-', width + 2) + "+";
         var content = string.Join(
            "\n",
            lines.Select(line => $"| {line.PadRight(width)} |")
         );

         AppendColoredText(
            logControl,
            $"{border}\n{content}\n{border}\n\n\n",
            AccentColor
         );
      }

      public static void AppendAccentMessage(
         RichTextBox logControl,
         string message
      )
      {
         AppendColoredText(logControl, message, AccentColor);
      }

      private static void AppendColoredText(
         RichTextBox logControl,
         string text,
         Color color
      )
      {
         logControl.SelectionStart = logControl.TextLength;
         logControl.SelectionLength = 0;
         logControl.SelectionColor = color;
         logControl.AppendText(text);
         logControl.SelectionColor = logControl.ForeColor;
         logControl.ScrollToCaret();
      }

      private static bool IsSyncedMessage(string msg)
      {
         return msg.StartsWith("Synced ", StringComparison.Ordinal);
      }
   }
}
