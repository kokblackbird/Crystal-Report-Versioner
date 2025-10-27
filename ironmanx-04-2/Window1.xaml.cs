using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace ironmanx_04_2
{
 /// <summary>
 /// Interaction logic for Window1.xaml
 /// </summary>
 public partial class Window1 : Window
 {
 private string _lastArchiveFolder;

 public Window1()
 {
 InitializeComponent();
 ShowCrystalRuntimeNoticeIfMissing();
 }

 private void ShowCrystalRuntimeNoticeIfMissing()
 {
 string installedVersion;
 if (!CrystalAnalyzer.TryGetInstalledRuntimeVersion(out installedVersion))
 {
 var url = "https://origin.softwaredownloads.sap.com/public/site/index.html";
 var msg = "SAP Crystal Reports .NET runtime was not detected. Advanced Crystal-level analysis will be disabled.\n\n" +
 "You can install the SAP Crystal Reports runtime (CRRuntime_64bit_13_0_xx).\n\n" +
 "Open the official SAP download page now?";
 var result = MessageBox.Show(this, msg, "Crystal Reports runtime not detected", MessageBoxButton.YesNo, MessageBoxImage.Information);
 if (result == MessageBoxResult.Yes)
 {
 try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); }
 catch { /* ignore */ }
 }
 return;
 }

 string reason;
 if (!CrystalAnalyzer.IsRuntimeAvailable(out reason))
 {
 var msg = string.Format("SAP Crystal Reports runtime detected (v{0}).\n\n" +
 "However, it could not be loaded by this application. The SAP Crystal Decisions assemblies target .NET Framework and may not be usable in some contexts.\n\n" +
 "Binary diff will still work, but Crystal-level analysis might be disabled.", installedVersion);
 MessageBox.Show(this, msg, "Crystal Reports runtime detected but not usable in-process", MessageBoxButton.OK, MessageBoxImage.Information);
 }
 }

 private void BrowseOriginal_Click(object sender, RoutedEventArgs e)
 {
 var dlg = new OpenFileDialog
 {
 Filter = "Crystal Report (*.rpt)|*.rpt|All files (*.*)|*.*",
 Title = "Select original Crystal Report (.rpt)"
 };
 if (dlg.ShowDialog(this).GetValueOrDefault())
 {
 OriginalPathText.Text = dlg.FileName;
 // Auto-suggest archive root based on original report path
 try
 {
 var baseDir = Path.GetDirectoryName(dlg.FileName);
 if (!string.IsNullOrWhiteSpace(baseDir))
 {
 var defaultRoot = Path.Combine(baseDir, "archive");
 ArchiveRootText.Text = defaultRoot;
 }
 }
 catch { }
 }
 }

 private void BrowseChanged_Click(object sender, RoutedEventArgs e)
 {
 var dlg = new OpenFileDialog
 {
 Filter = "Crystal Report (*.rpt)|*.rpt|All files (*.*)|*.*",
 Title = "Select changed Crystal Report (.rpt)"
 };
 if (dlg.ShowDialog(this).GetValueOrDefault())
 {
 ChangedPathText.Text = dlg.FileName;
 }
 }

 private void BrowseArchiveRoot_Click(object sender, RoutedEventArgs e)
 {
 string picked;
 if (FolderPicker.TryPickFolder(this, "Select archive root folder", out picked))
 {
 ArchiveRootText.Text = picked ?? string.Empty;
 }
 }

 private async void ArchiveButton_Click(object sender, RoutedEventArgs e)
 {
 try
 {
 Log("Starting archive...");
 var originalPath = (OriginalPathText.Text ?? string.Empty).Trim();
 var changedPath = (ChangedPathText.Text ?? string.Empty).Trim();
 var archiveRoot = (ArchiveRootText.Text ?? string.Empty).Trim();
 var message = (MessageText.Text ?? string.Empty).Trim();

 if (!File.Exists(originalPath)) { Warn("Original file not found."); return; }
 if (!File.Exists(changedPath)) { Warn("Changed file not found."); return; }

 // Auto-derive archive root if empty
 if (string.IsNullOrWhiteSpace(archiveRoot))
 {
 var baseDir = Path.GetDirectoryName(originalPath) ?? string.Empty;
 archiveRoot = Path.Combine(baseDir, "archive");
 ArchiveRootText.Text = archiveRoot;
 }

 // Report folder uses original report name
 var reportKey = Path.GetFileNameWithoutExtension(originalPath);
 var reportRoot = Path.Combine(archiveRoot, SanitizePath(reportKey));
 Directory.CreateDirectory(reportRoot);

 var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
 var versionFolder = Path.Combine(reportRoot, "v" + stamp);
 Directory.CreateDirectory(versionFolder);

 var originalFolder = Path.Combine(versionFolder, "original");
 var changedFolder = Path.Combine(versionFolder, "changed");
 Directory.CreateDirectory(originalFolder);
 Directory.CreateDirectory(changedFolder);

 var originalCopy = Path.Combine(originalFolder, Path.GetFileName(originalPath));
 var changedCopy = Path.Combine(changedFolder, Path.GetFileName(changedPath));
 File.Copy(originalPath, originalCopy, true);
 File.Copy(changedPath, changedCopy, true);

 Log("Files copied.");

 var basicDiff = await BinaryDiff.ComputeAsync(originalCopy, changedCopy);
 var basicHtml = basicDiff.ToHtml();

 List<string> analyzerLog;
 var crystalHtml = CrystalAnalyzer.TryAnalyze(originalCopy, changedCopy, out analyzerLog);
 foreach (var line in analyzerLog) Log(line);

 if (string.IsNullOrWhiteSpace(crystalHtml))
 {
 string helperHtml, helperJson, helperError;
 if (CrystalHelperRunner.TryRun(originalCopy, changedCopy, versionFolder, out helperHtml, out helperJson, out helperError))
 {
 crystalHtml = helperHtml;
 if (!string.IsNullOrEmpty(helperJson))
 {
 File.WriteAllText(Path.Combine(versionFolder, "crystal_diff.json"), helperJson, Encoding.UTF8);
 }
 Log("Crystal helper completed.");
 }
 else if (!string.IsNullOrEmpty(helperError))
 {
 Log("Crystal helper failed: " + helperError);
 }
 }

 var changes = HtmlBuilder.BuildReadme(reportKey, message, originalCopy, changedCopy, basicHtml, crystalHtml);
 var changesPath = Path.Combine(versionFolder, "changes.html");
 File.WriteAllText(changesPath, changes, Encoding.UTF8);

 // metadata.json (simple hand-made JSON to avoid extra packages)
 var via = string.IsNullOrWhiteSpace(crystalHtml) ? "none" : (analyzerLog != null && analyzerLog.Count >0 ? "inproc" : "helper");
 var metaJson = BuildMetaJson(reportKey, message, basicDiff, originalCopy, changedCopy, via);
 File.WriteAllText(Path.Combine(versionFolder, "metadata.json"), metaJson, Encoding.UTF8);

 // Compress version folder to zip to conserve space
 var zipPath = Path.Combine(reportRoot, reportKey + "-" + stamp + ".zip");
 string zipErr;
 string artifactLink = null;
 if (TryZipDirectory(versionFolder, zipPath, out zipErr))
 {
 Log("Created archive zip: " + zipPath);
 artifactLink = Path.GetFileName(zipPath);
 try
 {
 Directory.Delete(versionFolder, true);
 Log("Removed uncompressed folder to save space.");
 }
 catch (Exception delEx)
 {
 Log("Warning: could not delete folder: " + delEx.Message);
 }
 _lastArchiveFolder = reportRoot; // zip lives under report root
 }
 else
 {
 Log("Zip failed: " + zipErr);
 artifactLink = versionFolder; // fallback
 _lastArchiveFolder = versionFolder;
 }

 // Append to master changelog in report root
 Changelog.AppendEntry(
 reportRoot,
 reportKey,
 stamp,
 message,
 Path.GetFileName(originalCopy),
 Path.GetFileName(changedCopy),
 basicHtml,
 crystalHtml,
 artifactLink);

 // Replace original with changed now that archiving and logging are complete
 try
 {
 if (!string.Equals(originalPath, changedPath, StringComparison.OrdinalIgnoreCase))
 {
 File.Copy(changedPath, originalPath, true);
 Log("Replaced original report with changed report.");
 }
 else
 {
 Log("Original and changed paths are identical; replacement skipped.");
 }
 }
 catch (Exception rex)
 {
 Log("Failed to replace original report: " + rex.Message);
 }

 Log("Archive complete: " + (_lastArchiveFolder ?? versionFolder));
 }
 catch (Exception ex)
 {
 Warn("Archive failed: " + ex.Message);
 }
 }

 private void OpenArchiveButton_Click(object sender, RoutedEventArgs e)
 {
 var path = !string.IsNullOrEmpty(_lastArchiveFolder) ? _lastArchiveFolder : (ArchiveRootText.Text ?? string.Empty).Trim();
 if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
 {
 Warn("No archive folder to open.");
 return;
 }
 try
 {
 Process.Start(new ProcessStartInfo
 {
 FileName = path,
 UseShellExecute = true
 });
 }
 catch (Exception ex)
 {
 Warn("Failed to open folder: " + ex.Message);
 }
 }

 private void Log(string message)
 {
 if (LogText == null) return;
 LogText.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
 LogText.ScrollToEnd();
 }

 private void Warn(string message)
 {
 Log(message);
 MessageBox.Show(this, message, "Crystal Archiver", MessageBoxButton.OK, MessageBoxImage.Warning);
 }

 private static string SanitizePath(string input)
 {
 var invalid = Path.GetInvalidFileNameChars();
 var sb = new StringBuilder(input.Length);
 foreach (var ch in input)
 {
 bool isInvalid = false;
 for (int i =0; i < invalid.Length; i++)
 {
 if (invalid[i] == ch) { isInvalid = true; break; }
 }
 sb.Append(isInvalid ? '_' : ch);
 }
 return sb.ToString();
 }

 private static string BuildMetaJson(string reportKey, string message, BinaryDiff diff, string originalCopy, string changedCopy, string via)
 {
 Func<string, string> esc = s =>
 (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");

 var sb = new StringBuilder();
 sb.Append("{\n");
 sb.AppendFormat(" \"report\": \"{0}\",\n", esc(reportKey));
 sb.AppendFormat(" \"createdUtc\": \"{0}\",\n", DateTime.UtcNow.ToString("o"));
 sb.AppendFormat(" \"message\": \"{0}\",\n", esc(message));
 sb.Append(" \"original\": {\n");
 sb.AppendFormat(" \"path\": \"{0}\",\n", esc(Path.GetFileName(originalCopy)));
 sb.AppendFormat(" \"sha256\": \"{0}\",\n", esc(diff.OriginalSha256));
 sb.AppendFormat(" \"size\": {0}\n", diff.OriginalSize);
 sb.Append(" },\n");
 sb.Append(" \"changed\": {\n");
 sb.AppendFormat(" \"path\": \"{0}\",\n", esc(Path.GetFileName(changedCopy)));
 sb.AppendFormat(" \"sha256\": \"{0}\",\n", esc(diff.ChangedSha256));
 sb.AppendFormat(" \"size\": {0}\n", diff.ChangedSize);
 sb.Append(" },\n");
 sb.Append(" \"diff\": {\n");
 sb.AppendFormat(" \"differentBytes\": {0},\n", diff.DifferentBytes);
 sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, " \"differencePercent\": {0:F2}\n", diff.DifferencePercent);
 sb.Append(" },\n");
 sb.AppendFormat(" \"crystal\": {{ \"via\": \"{0}\" }}\n", esc(via));
 sb.Append("}\n");
 return sb.ToString();
 }

 private static bool TryZipDirectory(string sourceDirectory, string zipPath, out string error)
 {
 try
 {
 // Ensure target directory exists
 var targetDir = Path.GetDirectoryName(zipPath);
 if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

 // Load ZipFile from System.IO.Compression.FileSystem at runtime to avoid project reference changes
 var asm = Assembly.Load("System.IO.Compression.FileSystem");
 var zipType = asm.GetType("System.IO.Compression.ZipFile");
 var mi = zipType.GetMethod("CreateFromDirectory", new Type[] { typeof(string), typeof(string) });
 if (mi == null)
 {
 error = "ZipFile.CreateFromDirectory overload not found.";
 return false;
 }
 mi.Invoke(null, new object[] { sourceDirectory, zipPath });
 error = null;
 return true;
 }
 catch (Exception ex)
 {
 error = ex.Message;
 return false;
 }
 }
 private void RevertButton_Click(object sender, RoutedEventArgs e)
 {
 try
 {
 var originalPath = (OriginalPathText.Text ?? string.Empty).Trim();
 var archiveRoot = (ArchiveRootText.Text ?? string.Empty).Trim();
 if (!File.Exists(originalPath)) { Warn("Original file not found."); return; }
 if (string.IsNullOrWhiteSpace(archiveRoot))
 {
 var baseDir = Path.GetDirectoryName(originalPath) ?? string.Empty;
 archiveRoot = Path.Combine(baseDir, "archive");
 }

 var reportKey = Path.GetFileNameWithoutExtension(originalPath);
 var reportRoot = Path.Combine(archiveRoot, SanitizePath(reportKey));
 if (!Directory.Exists(reportRoot)) { Warn("No archive found for this report."); return; }

 // Find the latest version artifact (zip preferred)
 var latest = FindLatestVersionArtifact(reportRoot);
 if (string.IsNullOrEmpty(latest)) { Warn("No version artifacts found to revert."); return; }

 string tempRestoreFolder = null;
 string restoredOriginalPath = null;
 try
 {
 if (latest.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
 {
 tempRestoreFolder = Path.Combine(reportRoot, ".restore-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
 Directory.CreateDirectory(tempRestoreFolder);
 string unzipErr;
 if (!TryUnzip(latest, tempRestoreFolder, out unzipErr)) { Warn("Failed to unzip: " + unzipErr); return; }
 restoredOriginalPath = Path.Combine(tempRestoreFolder, "original", Path.GetFileName(originalPath));
 }
 else
 {
 restoredOriginalPath = Path.Combine(latest, "original", Path.GetFileName(originalPath));
 }

 if (!File.Exists(restoredOriginalPath)) { Warn("Original file not found inside archive."); return; }

 // Backup current and replace
 try { File.Copy(originalPath, originalPath + ".bak", true); } catch { }
 File.Copy(restoredOriginalPath, originalPath, true);
 Log("Reverted to previous version.");

 // Append a revert entry to changelog
 var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
 var message = "Reverted to previous version";
 var basicHtml = "<p class='muted'>Reversion operation. See previous entries for diff details.</p>";
 Changelog.AppendEntry(reportRoot, reportKey, stamp, message, Path.GetFileName(originalPath), Path.GetFileName(originalPath), basicHtml, null, null);
 }
 finally
 {
 if (!string.IsNullOrEmpty(tempRestoreFolder))
 {
 try { Directory.Delete(tempRestoreFolder, true); } catch { }
 }
 }
 }
 catch (Exception ex)
 {
 Warn("Revert failed: " + ex.Message);
 }
 }

 private static string FindLatestVersionArtifact(string reportRoot)
 {
 // Prefer zips, then folders. Sort by detected timestamp in name, fallback to last write.
 var zips = Directory.GetFiles(reportRoot, "*.zip");
 var folders = Directory.GetDirectories(reportRoot, "v*");
 var candidates = new List<string>();
 candidates.AddRange(zips);
 candidates.AddRange(folders);
 if (candidates.Count ==0) return null;

 DateTime Score(string path)
 {
 var name = Path.GetFileName(path);
 var withoutExt = Path.GetFileNameWithoutExtension(path);
 string token = null;
 if (name.StartsWith("v", StringComparison.OrdinalIgnoreCase)) token = name.Substring(1); // vyyyyMMdd-HHmmss
 else
 {
 var dash = withoutExt.LastIndexOf('-');
 if (dash >=0 && dash +1 < withoutExt.Length) token = withoutExt.Substring(dash +1);
 }
 if (DateTime.TryParseExact(token, "yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
 return dt;
 return File.GetLastWriteTimeUtc(path);
 }

 return candidates.OrderByDescending(Score).First();
 }

 private static bool TryUnzip(string zipPath, string destination, out string error)
 {
 try
 {
 var asm = Assembly.Load("System.IO.Compression.FileSystem");
 var zipType = asm.GetType("System.IO.Compression.ZipFile");
 var mi = zipType.GetMethod("ExtractToDirectory", new Type[] { typeof(string), typeof(string) });
 if (mi == null) { error = "ZipFile.ExtractToDirectory overload not found."; return false; }
 mi.Invoke(null, new object[] { zipPath, destination });
 error = null; return true;
 }
 catch (Exception ex) { error = ex.Message; return false; }
 }
 }

 internal static class HtmlBuilder
 {
 public static string BuildReadme(string reportKey, string message, string originalPath, string changedPath, string basicDiffHtml, string crystalHtml)
 {
 var now = DateTime.UtcNow.ToString("u");
 var oInfo = new FileInfo(originalPath);
 var cInfo = new FileInfo(changedPath);
 var sb = new StringBuilder();
 sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'/><title>");
 sb.Append(E(reportKey));
 sb.Append(" - Version</title><style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;} h1,h2{font-weight:600} code,pre{background:#0e1a21;color:#e6f9ff;padding:2px4px;border-radius:4px} .grid{display:grid;grid-template-columns:1fr1fr;gap:16px} .section{margin-top:24px} table{border-collapse:collapse} td,th{border:1px solid #1f3a46;padding:6px8px} .muted{color:#a5c7d1} body{background:#0d1b22;color:#e6f9ff}</style></head><body>");
 sb.Append("<h1>" + E(reportKey) + " - Version</h1>");
 if (!string.IsNullOrWhiteSpace(message))
 {
 sb.Append("<p><strong>Message:</strong> " + E(message) + "</p>");
 }
 sb.Append("<p class='muted'>Generated " + E(now) + " (UTC)</p>");

 sb.Append("<div class='section'><h2>Inputs</h2><div class='grid'>");
 sb.Append("<div><h3>Original</h3><ul><li>File: <code>" + E(Path.GetFileName(originalPath)) + "</code></li><li>Size: " + oInfo.Length.ToString("N0") + " bytes</li><li>Modified: " + oInfo.LastWriteTimeUtc.ToString("u") + " UTC</li></ul></div>");
 sb.Append("<div><h3>Changed</h3><ul><li>File: <code>" + E(Path.GetFileName(changedPath)) + "</code></li><li>Size: " + cInfo.Length.ToString("N0") + " bytes</li><li>Modified: " + cInfo.LastWriteTimeUtc.ToString("u") + " UTC</li></ul></div>");
 sb.Append("</div></div>");

 sb.Append("<div class='section'><h2>Binary diff summary</h2>");
 sb.Append(basicDiffHtml);
 sb.Append("</div>");

 if (!string.IsNullOrWhiteSpace(crystalHtml))
 {
 sb.Append("<div class='section'><h2>Crystal Reports analysis</h2>");
 sb.Append(crystalHtml);
 sb.Append("</div>");
 }
 else
 {
 sb.Append("<div class='section'><h2>Crystal Reports analysis</h2><p class='muted'>Crystal Reports runtime was not available. Only binary-level details are shown.</p></div>");
 }

 sb.Append("</body></html>");
 return sb.ToString();
 }

 private static string E(string s)
 {
 return System.Net.WebUtility.HtmlEncode(s ?? string.Empty);
 }
 }

 internal sealed class BinaryDiff
 {
 public string OriginalSha256 { get; private set; }
 public string ChangedSha256 { get; private set; }
 public long OriginalSize { get; private set; }
 public long ChangedSize { get; private set; }
 public long DifferentBytes { get; private set; }
 public double DifferencePercent { get; private set; }

 public static Task<BinaryDiff> ComputeAsync(string originalPath, string changedPath)
 {
 return Task.Run(() =>
 {
 var diff = new BinaryDiff();

 using (var o = File.OpenRead(originalPath))
 using (var c = File.OpenRead(changedPath))
 {
 diff.OriginalSize = o.Length;
 diff.ChangedSize = c.Length;

 using (var sha = SHA256.Create())
 {
 diff.OriginalSha256 = ToHex(sha.ComputeHash(o));
 o.Position =0;
 diff.ChangedSha256 = ToHex(sha.ComputeHash(c));
 c.Position =0;
 }

 var bufO = new byte[81920];
 var bufC = new byte[81920];
 int readO, readC;
 do
 {
 readO = o.Read(bufO,0, bufO.Length);
 readC = c.Read(bufC,0, bufC.Length);
 var max = Math.Max(readO, readC);
 for (int i =0; i < max; i++)
 {
 var bo = i < readO ? bufO[i] : (byte)0;
 var bc = i < readC ? bufC[i] : (byte)0;
 if (bo != bc) diff.DifferentBytes++;
 }
 } while (readO >0 || readC >0);
 }

 var baseCount = Math.Max(diff.OriginalSize, diff.ChangedSize);
 diff.DifferencePercent = baseCount ==0 ?0 : (double)diff.DifferentBytes / (double)baseCount *100.0;
 return diff;
 });
 }

 public string ToHtml()
 {
 var sb = new StringBuilder();
 sb.Append("<table><thead><tr><th></th><th>Original</th><th>Changed</th></tr></thead><tbody>");
 sb.Append("<tr><td>SHA-256</td><td><code>" + OriginalSha256 + "</code></td><td><code>" + ChangedSha256 + "</code></td></tr>");
 sb.Append("<tr><td>Size</td><td>" + OriginalSize.ToString("N0") + " bytes</td><td>" + ChangedSize.ToString("N0") + " bytes</td></tr>");
 sb.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, "<tr><td>Different bytes</td><td colspan='2'>{0:N0} ({1:F2}% of larger file)</td></tr>", DifferentBytes, DifferencePercent));
 sb.Append("</tbody></table>");
 return sb.ToString();
 }

 private static string ToHex(byte[] bytes)
 {
 var sb = new StringBuilder(bytes.Length *2);
 for (int i =0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
 return sb.ToString().ToUpperInvariant();
 }
 }

 internal static class CrystalAnalyzer
 {
 public static string TryAnalyze(string originalPath, string changedPath, out List<string> logger)
 {
 logger = new List<string>();
 try
 {
 var asm = TryLoadCrystalAssembly(logger);
 if (asm == null)
 {
 logger.Add("CrystalDecisions assemblies not found. Skipping deep analysis.");
 return null;
 }

 var reportDocType = asm.GetType("CrystalDecisions.CrystalReports.Engine.ReportDocument");
 if (reportDocType == null)
 {
 logger.Add("ReportDocument type not found in CrystalDecisions assembly.");
 return null;
 }

 object docO = null, docC = null;
 try
 {
 docO = Activator.CreateInstance(reportDocType);
 docC = Activator.CreateInstance(reportDocType);
 var load = reportDocType.GetMethod("Load", new[] { typeof(string) });
 if (load != null)
 {
 load.Invoke(docO, new object[] { originalPath });
 load.Invoke(docC, new object[] { changedPath });
 }

 var html = BuildCrystalDiffHtml(reportDocType, docO, docC, logger);
 return html;
 }
 finally
 {
 TryDisposeCrystal(reportDocType, docO);
 TryDisposeCrystal(reportDocType, docC);
 }
 }
 catch (Exception ex)
 {
 logger.Add("Crystal analysis failed: " + ex.Message);
 return null;
 }
 }

 public static bool IsRuntimeAvailable(out string reason)
 {
 reason = string.Empty;
 try
 {
 var asm = TryLoadCrystalAssembly(null);
 if (asm == null)
 {
 reason = "CrystalDecisions assemblies not found.";
 return false;
 }
 var reportDocType = asm.GetType("CrystalDecisions.CrystalReports.Engine.ReportDocument");
 if (reportDocType == null)
 {
 reason = "ReportDocument type not found in CrystalDecisions assembly.";
 return false;
 }
 return true;
 }
 catch (Exception ex)
 {
 reason = ex.Message;
 return false;
 }
 }

 public static bool TryGetInstalledRuntimeVersion(out string version)
 {
 version = string.Empty;
 if (TryReadCrVersionFromRegistry(Microsoft.Win32.RegistryView.Registry64, out version) ||
 TryReadCrVersionFromRegistry(Microsoft.Win32.RegistryView.Registry32, out version))
 {
 return true;
 }
 if (TryReadCrVersionFromUninstall(Microsoft.Win32.RegistryView.Registry64, out version) ||
 TryReadCrVersionFromUninstall(Microsoft.Win32.RegistryView.Registry32, out version))
 {
 return true;
 }
 return false;
 }

 private static bool TryReadCrVersionFromRegistry(Microsoft.Win32.RegistryView view, out string version)
 {
 version = string.Empty;
 try
 {
 using (var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, view))
 {
 var subKeys = new[]
 {
 @"SOFTWARE\SAP BusinessObjects\Crystal Reports for .NET Framework4.0\Crystal Reports",
 @"SOFTWARE\SAP BusinessObjects\Crystal Reports for .NET Framework4.0\Redist",
 @"SOFTWARE\SAP BusinessObjects\Crystal Reports for .NET Framework4.0"
 };
 foreach (var sub in subKeys)
 {
 using (var key = baseKey.OpenSubKey(sub))
 {
 object val = key == null ? null : (key.GetValue("Version") ?? key.GetValue("ProductVersion") ?? key.GetValue("DisplayVersion"));
 var s = val as string;
 if (!string.IsNullOrWhiteSpace(s)) { version = s; return true; }
 }
 }
 }
 }
 catch { }
 return false;
 }

 private static bool TryReadCrVersionFromUninstall(Microsoft.Win32.RegistryView view, out string version)
 {
 version = string.Empty;
 try
 {
 using (var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, view))
 using (var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
 {
 if (uninstall == null) return false;
 foreach (var subName in uninstall.GetSubKeyNames())
 {
 using (var sub = uninstall.OpenSubKey(subName))
 {
 var displayName = sub == null ? null : sub.GetValue("DisplayName") as string;
 if (string.IsNullOrWhiteSpace(displayName)) continue;
 if (displayName.IndexOf("Crystal Reports runtime engine for .NET Framework", StringComparison.OrdinalIgnoreCase) >=0 ||
 displayName.IndexOf("SAP Crystal Reports runtime engine", StringComparison.OrdinalIgnoreCase) >=0)
 {
 var v = sub.GetValue("DisplayVersion") as string;
 if (!string.IsNullOrWhiteSpace(v)) { version = v; return true; }
 }
 }
 }
 }
 }
 catch { }
 return false;
 }

 private static System.Reflection.Assembly TryLoadCrystalAssembly(List<string> logger)
 {
 var names = new[] { "CrystalDecisions.CrystalReports.Engine", "CrystalDecisions.Shared" };
 try
 {
 for (int i =0; i < names.Length; i++)
 {
 try { System.Reflection.Assembly.Load(new System.Reflection.AssemblyName(names[i])); }
 catch (Exception ex) { if (logger != null) logger.Add("Assembly load failed for " + names[i] + ": " + ex.Message); }
 }
 var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => string.Equals(a.GetName().Name, "CrystalDecisions.CrystalReports.Engine", StringComparison.OrdinalIgnoreCase));
 return asm;
 }
 catch (Exception ex)
 {
 if (logger != null) logger.Add("Failed probing Crystal assemblies: " + ex.Message);
 return null;
 }
 }

 private static void TryDisposeCrystal(Type reportDocType, object instance)
 {
 if (instance == null) return;
 try
 {
 var close = reportDocType.GetMethod("Close", Type.EmptyTypes);
 if (close != null) close.Invoke(instance, null);
 var disp = instance as IDisposable;
 if (disp != null) disp.Dispose();
 }
 catch { /* ignore */ }
 }

 private static string BuildCrystalDiffHtml(Type reportDocType, object docO, object docC, List<string> logger)
 {
 try
 {
 var sb = new StringBuilder();
 var rsfProp = reportDocType.GetProperty("RecordSelectionFormula");
 var rsfO = rsfProp != null ? (rsfProp.GetValue(docO, null) as string ?? string.Empty) : string.Empty;
 var rsfC = rsfProp != null ? (rsfProp.GetValue(docC, null) as string ?? string.Empty) : string.Empty;
 if (!string.Equals(rsfO, rsfC, StringComparison.Ordinal))
 {
 sb.Append("<h3>Record Selection Formula</h3>");
 sb.Append("<table><tr><th>Original</th><th>Changed</th></tr>");
 sb.Append("<tr><td><pre>" + System.Net.WebUtility.HtmlEncode(rsfO) + "</pre></td><td><pre>" + System.Net.WebUtility.HtmlEncode(rsfC) + "</pre></td></tr></table>");
 }

 var dataDefProp = reportDocType.GetProperty("DataDefinition");
 var dataDef = dataDefProp != null ? dataDefProp.GetValue(docO, null) : null;
 var dataDefC = dataDefProp != null ? dataDefProp.GetValue(docC, null) : null;
 if (dataDef != null && dataDefC != null)
 {
 AppendNameTextCollectionDiff(sb, dataDef, dataDefC, "FormulaFields", "Text");
 AppendParameterFieldsDiff(sb, dataDef, dataDefC);
 }

 var dbProp = reportDocType.GetProperty("Database");
 var dbO = dbProp != null ? dbProp.GetValue(docO, null) : null;
 var dbC = dbProp != null ? dbProp.GetValue(docC, null) : null;
 if (dbO != null && dbC != null)
 {
 AppendTablesDiff(sb, dbO, dbC);
 }

 if (sb.Length ==0)
 {
 sb.Append("<p class='muted'>No Crystal-level differences detected (or features not supported by runtime).</p>");
 }
 return sb.ToString();
 }
 catch (Exception ex)
 {
 if (logger != null) logger.Add("Crystal diff rendering failed: " + ex.Message);
 return "<p class='muted'>Crystal analysis encountered an error.</p>";
 }
 }

 private static void AppendNameTextCollectionDiff(StringBuilder sb, object dataDefO, object dataDefC, string collectionName, string textProperty)
 {
 var collPropO = dataDefO.GetType().GetProperty(collectionName);
 var collPropC = dataDefC.GetType().GetProperty(collectionName);
 var collO = collPropO != null ? collPropO.GetValue(dataDefO, null) : null;
 var collC = collPropC != null ? collPropC.GetValue(dataDefC, null) : null;
 if (collO == null || collC == null) return;

 var enumO = collO as System.Collections.IEnumerable;
 var enumC = collC as System.Collections.IEnumerable;
 var mapO = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
 var mapC = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

 foreach (var item in enumO)
 {
 var t = item.GetType();
 var nameProp = t.GetProperty("Name");
 var textProp = t.GetProperty(textProperty);
 var name = nameProp != null ? (nameProp.GetValue(item, null) as string ?? string.Empty) : string.Empty;
 var txt = textProp != null ? (textProp.GetValue(item, null) as string ?? string.Empty) : string.Empty;
 if (!string.IsNullOrEmpty(name)) mapO[name] = txt;
 }
 foreach (var item in enumC)
 {
 var t = item.GetType();
 var nameProp = t.GetProperty("Name");
 var textProp = t.GetProperty(textProperty);
 var name = nameProp != null ? (nameProp.GetValue(item, null) as string ?? string.Empty) : string.Empty;
 var txt = textProp != null ? (textProp.GetValue(item, null) as string ?? string.Empty) : string.Empty;
 if (!string.IsNullOrEmpty(name)) mapC[name] = txt;
 }

 var added = mapC.Keys.Except(mapO.Keys, StringComparer.OrdinalIgnoreCase).ToList();
 var removed = mapO.Keys.Except(mapC.Keys, StringComparer.OrdinalIgnoreCase).ToList();
 var possiblyChanged = mapC.Keys.Intersect(mapO.Keys, StringComparer.OrdinalIgnoreCase).Where(k => !string.Equals(mapC[k], mapO[k], StringComparison.Ordinal)).ToList();

 if (added.Count + removed.Count + possiblyChanged.Count ==0) return;

 sb.Append("<h3>" + System.Net.WebUtility.HtmlEncode(collectionName) + "</h3>");
 if (added.Count >0)
 {
 sb.Append("<h4>Added</h4><ul>");
 foreach (var k in added) sb.Append("<li><code>" + System.Net.WebUtility.HtmlEncode(k) + "</code></li>");
 sb.Append("</ul>");
 }
 if (removed.Count >0)
 {
 sb.Append("<h4>Removed</h4><ul>");
 foreach (var k in removed) sb.Append("<li><code>" + System.Net.WebUtility.HtmlEncode(k) + "</code></li>");
 sb.Append("</ul>");
 }
 if (possiblyChanged.Count >0)
 {
 sb.Append("<h4>Changed</h4><table><tr><th>Name</th><th>Original</th><th>Changed</th></tr>");
 foreach (var k in possiblyChanged)
 {
 sb.Append("<tr><td><code>" + System.Net.WebUtility.HtmlEncode(k) + "</code></td><td><pre>" + System.Net.WebUtility.HtmlEncode(mapO[k]) + "</pre></td><td><pre>" + System.Net.WebUtility.HtmlEncode(mapC[k]) + "</pre></td></tr>");
 }
 sb.Append("</table>");
 }
 }

 private static void AppendParameterFieldsDiff(StringBuilder sb, object dataDefO, object dataDefC)
 {
 var collPropO = dataDefO.GetType().GetProperty("ParameterFields");
 var collPropC = dataDefC.GetType().GetProperty("ParameterFields");
 var collO = collPropO != null ? collPropO.GetValue(dataDefO, null) : null;
 var collC = collPropC != null ? collPropC.GetValue(dataDefC, null) : null;
 if (collO == null || collC == null) return;

 var enumO = collO as System.Collections.IEnumerable;
 var enumC = collC as System.Collections.IEnumerable;
 var mapO = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
 var mapC = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
 foreach (var item in enumO)
 {
 var t = item.GetType();
 var nameProp = t.GetProperty("Name");
 var typeProp = t.GetProperty("ParameterValueType");
 var name = nameProp != null ? (nameProp.GetValue(item, null) as string ?? string.Empty) : string.Empty;
 var typeName = typeProp != null ? (typeProp.GetValue(item, null) == null ? string.Empty : typeProp.GetValue(item, null).ToString()) : string.Empty;
 if (!string.IsNullOrEmpty(name)) mapO[name] = typeName;
 }
 foreach (var item in enumC)
 {
 var t = item.GetType();
 var nameProp = t.GetProperty("Name");
 var typeProp = t.GetProperty("ParameterValueType");
 var name = nameProp != null ? (nameProp.GetValue(item, null) as string ?? string.Empty) : string.Empty;
 var typeName = typeProp != null ? (typeProp.GetValue(item, null) == null ? string.Empty : typeProp.GetValue(item, null).ToString()) : string.Empty;
 if (!string.IsNullOrEmpty(name)) mapC[name] = typeName;
 }

 var added = mapC.Keys.Except(mapO.Keys, StringComparer.OrdinalIgnoreCase).ToList();
 var removed = mapO.Keys.Except(mapC.Keys, StringComparer.OrdinalIgnoreCase).ToList();
 var changed = mapC.Keys.Intersect(mapO.Keys, StringComparer.OrdinalIgnoreCase).Where(k => !string.Equals(mapC[k], mapO[k], StringComparison.Ordinal)).ToList();

 if (added.Count + removed.Count + changed.Count ==0) return;

 sb.Append("<h3>ParameterFields</h3>");
 if (added.Count >0)
 {
 sb.Append("<h4>Added</h4><ul>");
 foreach (var k in added) sb.Append("<li><code>" + System.Net.WebUtility.HtmlEncode(k) + "</code> (" + System.Net.WebUtility.HtmlEncode(mapC[k]) + ")</li>");
 sb.Append("</ul>");
 }
 if (removed.Count >0)
 {
 sb.Append("<h4>Removed</h4><ul>");
 foreach (var k in removed) sb.Append("<li><code>" + System.Net.WebUtility.HtmlEncode(k) + "</code> (" + System.Net.WebUtility.HtmlEncode(mapO[k]) + ")</li>");
 sb.Append("</ul>");
 }
 if (changed.Count >0)
 {
 sb.Append("<h4>Changed</h4><table><tr><th>Name</th><th>Original Type</th><th>Changed Type</th></tr>");
 foreach (var k in changed)
 {
 sb.Append("<tr><td><code>" + System.Net.WebUtility.HtmlEncode(k) + "</code></td><td>" + System.Net.WebUtility.HtmlEncode(mapO[k]) + "</td><td>" + System.Net.WebUtility.HtmlEncode(mapC[k]) + "</td></tr>");
 }
 sb.Append("</table>");
 }
 }

 private static void AppendTablesDiff(StringBuilder sb, object dbO, object dbC)
 {
 var tablesPropO = dbO.GetType().GetProperty("Tables");
 var tablesPropC = dbC.GetType().GetProperty("Tables");
 var tablesO = tablesPropO != null ? tablesPropO.GetValue(dbO, null) as System.Collections.IEnumerable : null;
 var tablesC = tablesPropC != null ? tablesPropC.GetValue(dbC, null) as System.Collections.IEnumerable : null;
 if (tablesO == null || tablesC == null) return;

 Func<object, Tuple<string, string>> keyOf = table =>
 {
 var t = table.GetType();
 var nameProp = t.GetProperty("Name");
 var locProp = t.GetProperty("Location");
 var name = nameProp != null ? (nameProp.GetValue(table, null) as string ?? string.Empty) : string.Empty;
 var loc = locProp != null ? (locProp.GetValue(table, null) as string ?? string.Empty) : string.Empty;
 return Tuple.Create(name, loc);
 };

 var listO = new List<Tuple<string, string>>();
 var listC = new List<Tuple<string, string>>();
 foreach (var it in tablesO) listO.Add(keyOf(it));
 foreach (var it in tablesC) listC.Add(keyOf(it));

 var setO = new HashSet<string>(listO.Select(x => x.Item1 + "|" + x.Item2), StringComparer.OrdinalIgnoreCase);
 var setC = new HashSet<string>(listC.Select(x => x.Item1 + "|" + x.Item2), StringComparer.OrdinalIgnoreCase);

 var added = setC.Except(setO, StringComparer.OrdinalIgnoreCase).ToList();
 var removed = setO.Except(setC, StringComparer.OrdinalIgnoreCase).ToList();

 if (added.Count + removed.Count ==0) return;

 sb.Append("<h3>Database Tables</h3>");
 if (added.Count >0)
 {
 sb.Append("<h4>Added</h4><ul>");
 foreach (var k in added) sb.Append("<li>" + System.Net.WebUtility.HtmlEncode(k) + "</li>");
 sb.Append("</ul>");
 }
 if (removed.Count >0)
 {
 sb.Append("<h4>Removed</h4><ul>");
 foreach (var k in removed) sb.Append("<li>" + System.Net.WebUtility.HtmlEncode(k) + "</li>");
 sb.Append("</ul>");
 }
 }
 }

 internal static class CrystalHelperRunner
 {
 public static bool TryRun(string originalPath, string changedPath, string versionFolder, out string helperHtml, out string helperJson, out string error)
 {
 // Stub for .NET Framework helper process. Not implemented in this project.
 helperHtml = null;
 helperJson = null;
 error = "Helper not available.";
 return false;
 }
 }

 internal static class FolderPicker
 {
 // Simple SHBrowseForFolder-based picker to avoid extra references
 private const uint BIF_RETURNONLYFSDIRS =0x0001;
 private const uint BIF_DONTGOBELOWDOMAIN =0x0002;
 private const uint BIF_EDITBOX =0x0010;
 private const uint BIF_NEWDIALOGSTYLE =0x0040;

 [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
 private struct BROWSEINFO
 {
 public IntPtr hwndOwner;
 public IntPtr pidlRoot;
 public IntPtr pszDisplayName;
 [MarshalAs(UnmanagedType.LPTStr)] public string lpszTitle;
 public uint ulFlags;
 public IntPtr lpfn;
 public IntPtr lParam;
 public int iImage;
 }

 [DllImport("shell32.dll", CharSet = CharSet.Auto)]
 private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO bi);

 [DllImport("shell32.dll", CharSet = CharSet.Auto)]
 private static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder pszPath);

 public static bool TryPickFolder(Window owner, string title, out string path)
 {
 path = null;
 var buffer = new StringBuilder(260);
 IntPtr pidl = IntPtr.Zero;
 IntPtr displayNamePtr = IntPtr.Zero;
 try
 {
 displayNamePtr = Marshal.AllocHGlobal(260 * Marshal.SystemDefaultCharSize);
 var bi = new BROWSEINFO();
 bi.hwndOwner = owner != null ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;
 bi.pidlRoot = IntPtr.Zero;
 bi.pszDisplayName = displayNamePtr;
 bi.lpszTitle = title ?? "Select folder";
 bi.ulFlags = BIF_RETURNONLYFSDIRS | BIF_EDITBOX | BIF_NEWDIALOGSTYLE | BIF_DONTGOBELOWDOMAIN;
 bi.lpfn = IntPtr.Zero;
 bi.lParam = IntPtr.Zero;
 bi.iImage =0;

 pidl = SHBrowseForFolder(ref bi);
 if (pidl == IntPtr.Zero) return false; // canceled
 if (!SHGetPathFromIDList(pidl, buffer)) return false;
 var s = buffer.ToString();
 if (string.IsNullOrWhiteSpace(s)) return false;
 path = s;
 return true;
 }
 finally
 {
 if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
 if (displayNamePtr != IntPtr.Zero) Marshal.FreeHGlobal(displayNamePtr);
 }
 }
 }

 internal static class Changelog
 {
 public static void AppendEntry(string reportRoot, string reportKey, string stamp, string message, string originalFileName, string changedFileName, string basicDiffHtml, string crystalHtml, string artifactLink)
 {
 try
 {
 var safeReport = Sanitize(reportKey);
 var path = Path.Combine(reportRoot, "ChangeLog-" + safeReport + ".html");
 var section = BuildSection(reportKey, stamp, message, originalFileName, changedFileName, basicDiffHtml, crystalHtml, artifactLink);
 if (!File.Exists(path))
 {
 var html = BuildShell(reportKey, section);
 File.WriteAllText(path, html, Encoding.UTF8);
 return;
 }

 var existing = File.ReadAllText(path, Encoding.UTF8);
 var endTag = "</body>";
 var idx = existing.LastIndexOf(endTag, StringComparison.OrdinalIgnoreCase);
 if (idx >=0)
 {
 var updated = existing.Substring(0, idx) + section + existing.Substring(idx);
 File.WriteAllText(path, updated, Encoding.UTF8);
 }
 else
 {
 // No closing body, append everything
 File.AppendAllText(path, section + "\n</body></html>", Encoding.UTF8);
 }
 }
 catch
 {
 // swallow: changelog shouldn't block archiving
 }
 }

 private static string BuildShell(string reportKey, string firstSection)
 {
 var sb = new StringBuilder();
 sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'/><title>ChangeLog - ");
 sb.Append(E(reportKey));
 sb.Append("</title><style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;background:#0d1b22;color:#e6f9ff} h1,h2{font-weight:600} a{color:#7fdbff} code,pre{background:#0e1a21;color:#e6f9ff;padding:2px4px;border-radius:4px} .entry{border:1px solid #1f3a46;border-radius:6px;margin:16px0;padding:12px} .muted{color:#a5c7d1}</style></head><body>");
 sb.Append("<h1>Change Log - ");
 sb.Append(E(reportKey));
 sb.Append("</h1>");
 sb.Append(firstSection);
 sb.Append("</body></html>");
 return sb.ToString();
 }

 private static string BuildSection(string reportKey, string stamp, string message, string originalFileName, string changedFileName, string basicDiffHtml, string crystalHtml, string artifactLink)
 {
 var sb = new StringBuilder();
 sb.Append("<div class='entry'>");
 sb.Append("<h2>Version v");
 sb.Append(E(stamp));
 sb.Append("</h2>");
 if (!string.IsNullOrWhiteSpace(message))
 {
 sb.Append("<p><strong>Message:</strong> ");
 sb.Append(E(message));
 sb.Append("</p>");
 }
 sb.Append("<p class='muted'>Created UTC: ");
 sb.Append(E(DateTime.UtcNow.ToString("u")));
 sb.Append("</p>");
 sb.Append("<p>Original: <code>");
 sb.Append(E(originalFileName));
 sb.Append("</code> &nbsp; Changed: <code>");
 sb.Append(E(changedFileName));
 sb.Append("</code></p>");
 if (!string.IsNullOrEmpty(artifactLink))
 {
 var linkText = Path.GetFileName(artifactLink);
 sb.Append("<p>Package: <a href='");
 sb.Append(E(linkText));
 sb.Append("'>");
 sb.Append(E(linkText));
 sb.Append("</a></p>");
 }
 sb.Append("<div><h3>Binary diff</h3>");
 sb.Append(basicDiffHtml ?? "");
 sb.Append("</div>");
 if (!string.IsNullOrWhiteSpace(crystalHtml))
 {
 sb.Append("<div><h3>Crystal analysis</h3>");
 sb.Append(crystalHtml);
 sb.Append("</div>");
 }
 sb.Append("</div>");
 return sb.ToString();
 }

 private static string E(string s)
 {
 return System.Net.WebUtility.HtmlEncode(s ?? string.Empty);
 }
 private static string Sanitize(string input)
 {
 var invalid = Path.GetInvalidFileNameChars();
 var sb = new StringBuilder(input.Length);
 foreach (var ch in input)
 sb.Append(invalid.Contains(ch) ? '_' : ch);
 return sb.ToString();
 }
 }
}
