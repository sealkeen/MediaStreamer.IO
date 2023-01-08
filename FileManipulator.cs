using MediaStreamer.Domain;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using MediaStreamer.TagEditing;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.IO;
using MediaStreamer.Logging;
using Sealkeen.Abstractions;

namespace MediaStreamer.IO
{
    public class FileManipulator : IFileManipulator
    {
        //TODO: Split long methods into several parts
        //TODO: Implement "Play several songs" cross-platformely
        public FileManipulator(IDBRepository iDBAccess, ILogger logger)
        {
            DBAccess = iDBAccess;
            _logger = logger;
        }
        public IDBRepository DBAccess { get; set; }
        protected ILogger _logger;
        public static bool MoveFileToArtistDirectory = false;

        private static string TryMoveFileToArtistDirectory(string fileName, string artistFromFile, string titleFromFile,
            Action<string> errorAction = null)
        {
            return fileName;
            try
            {
                string parentDirectory = new FileInfo(fileName).Directory.Parent.FullName;
                var newDirectory = Path.Combine(parentDirectory, artistFromFile);
                if (!Directory.Exists(newDirectory))
                    Directory.CreateDirectory(newDirectory);
                string newFileName = Path.Combine(newDirectory + $"{artistFromFile}/{titleFromFile}" + YearAsTime() + GetExtension(fileName));
                File.Move(fileName, newFileName);
                fileName = newFileName;
                errorAction?.Invoke($" TryMoveFileToArtistDirectory: file moved: {newFileName}");
            }
            catch { }
            errorAction?.Invoke($" TryMoveFileToArtistDirectory: file not moved, returning... ");

            return fileName;
        }

        private static string GetExtension(string fileName)
        {
            if (fileName.IndexOf('.') > 0)
            {
                bool correctExtension = false;
                for (int i = fileName.Length - 1; i > fileName.IndexOf('.'); i--)
                {
                    if (!char.IsLetterOrDigit(fileName[i]))
                        correctExtension = false;
                }
                if (correctExtension)
                    return Path.GetExtension(fileName);
            }
            return ".mp3";
        }

        private static string YearAsTime()
        {
            return $" ({DateTime.Now.Hour.ToString().PadLeft(2, '0')}{DateTime.Now.Minute.ToString().PadLeft(2, '0')})";
        }

        /// <summary>
        /// Returns null if error occured.
        /// </summary>
        /// <param name="fileName">The path to the composition's file.</param>
        /// <param name="errorAction">An action that takes a string to log if something went wrong.</param>
        /// <returns></returns>
        public Composition DecomposeAudioFile(string fileName, Action<string> errorAction = null)
        {
            Composition newComposition;
            try
            {
                _logger?.LogInfo($"Passed to decompose: {fileName}, existing : {File.Exists(fileName)}");
                errorAction?.Invoke($"Passed to decompose: {fileName}, existing : {File.Exists(fileName)}");
                if (fileName == null || !System.IO.File.Exists(fileName))
                {
                    return null;
                }

                _logger?.LogInfo($"Creating TagLib");
                errorAction?.Invoke($"Creating TagLib");
                var tfile = TagLib.File.Create($"{fileName}");

                _logger?.LogInfo($"Fetching data from file...");
                errorAction?.Invoke($"Fetching data from file...");
                string artistFromFile = DMTagExtractor.TryGetArtistNameFromFile(tfile, errorAction);
                string genreFromFile = DMTagExtractor.TryGetGenreFromFile(tfile, errorAction);
                string titleFromFile = DMTagExtractor.TryGetTitleFromFile(tfile);
                string albumFromFile = DMTagExtractor.TryGetAlbumFromFile(tfile);
                TimeSpan duration = DMTagExtractor.TryGetDurationFromFile(tfile);
                long? yearFromFile = DMTagExtractor.TryGetYearFromFile(tfile);

                if (string.IsNullOrEmpty(artistFromFile) ||
                    string.IsNullOrEmpty(titleFromFile) ||
                    artistFromFile == "Unknown" ||
                    titleFromFile == "Unknown"
                    ) {
                    System.IO.FileInfo fI = new System.IO.FileInfo(fileName);
                    if ((newComposition = CreateNewComposition(fI.Name, fI.FullName, titleFromFile,
                        artistFromFile, genreFromFile, albumFromFile, duration, yearFromFile)) == null) {
                        errorAction?.Invoke($"The file does not have enough information to add a song.");
                        _logger?.LogError($"The file does not have enough information to add a song.");
                        return null;
                    }
                } else {
                    _logger?.LogInfo($"Adding Artist...");
                    errorAction?.Invoke($"Adding Artist...");
                    var artist = DBAccess.AddArtist(artistFromFile, errorAction);
                    _logger?.LogInfo($"Adding Genre...");
                    errorAction?.Invoke($"Adding Genre...");
                    var genre = DBAccess.AddGenreToArtist(artist, genreFromFile, errorAction); //error
                    _logger?.LogInfo($"Adding Album...");
                    errorAction?.Invoke($"Adding Album...");
                    var album = DBAccess.AddAlbum(artist.ArtistName, albumFromFile, yearFromFile, null, null, errorAction);
                    _logger?.LogInfo($"Adding Composition...");
                    errorAction?.Invoke($"Adding Composition...");
                    if (MoveFileToArtistDirectory)
                        fileName = TryMoveFileToArtistDirectory(fileName, artistFromFile, titleFromFile);

                    newComposition = DBAccess.AddComposition(artist, album, titleFromFile, duration, fileName, null, false, errorAction);
                    _logger?.LogInfo($"Composition is not null: {newComposition != null}, FilePath ok: {newComposition?.FilePath != null} ");
                    errorAction?.Invoke($"Composition is not null: {newComposition != null}, FilePath ok: {newComposition?.FilePath != null} ");
                }
                DBAccess.DB.SaveChanges();
                DMTagEditor.AddArtistToCompositionsSourceFile(newComposition.Artist.ArtistName, newComposition, errorAction);
                DMTagEditor.AddTitleToCompositionsSourceFile(newComposition.CompositionName, newComposition, errorAction);
                return newComposition;
            }
            catch (Exception ex)
            {
                _logger?.LogError("MediaStreamer.IO: " + ex.ToString() + ex.Message);
                errorAction?.Invoke("MediaStreamer.IO: " + ex.ToString() + ex.Message);
                return null;
            }
        }

        public bool DecomposeAudioFiles(List<string> audioFiles, Action<string> errorAction)
        {
            bool successfull = false;
            try
            {
                foreach (string audioFile in audioFiles)
                {
                    successfull = DecomposeAudioFile(audioFile, _logger?.GetLogInfoOrReturnNull()) != null ? true : false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError("MediaStreamer.IO: " + ex.Message);
                errorAction?.Invoke("MediaStreamer.IO: " + ex.Message);
            }
            return successfull;
        }

        public Composition CreateNewComposition(
            string fileName,
            string fullFileName,
            string titleFromFile,
            string artistFromFile,
            string genreFromFile,
            string albumFromFile,
            TimeSpan? duration,
            long? yearFromFile,
            Action<string> errorAction = null)
        {
            try
            {
                string artistName = ""; string compositionName = "";

                ResolveArtistTitleConflicts(fileName, titleFromFile, artistFromFile, ref artistName, ref compositionName);
                if (artistName.Length < 1 || compositionName.Length < 1) {
                    errorAction?.Invoke("The  artist / title are less than zero, returning...");
                    return null;
                }
                var compProps = ExcludeYearIfExists(ExcludeExtension(compositionName));
                compositionName = compProps.GetName();
                yearFromFile = yearFromFile ?? compProps.GetYear();

                _logger?.LogInfo($"Adding Artist...");
                var artist = DBAccess.AddArtist(artistName, errorAction);
                _logger?.LogInfo($"Adding Genre...");
                var genre = DBAccess.AddGenreToArtist(artist, genreFromFile, errorAction);
                if (artist.ArtistName == null || artist.ArtistName == string.Empty)
                    return null;
                _logger?.LogInfo($"Adding Album...");
                var album = DBAccess.AddAlbum(artist, genre, albumFromFile, null, null, yearFromFile, errorAction);
                _logger?.LogInfo($"Adding Composition...");

                if (MoveFileToArtistDirectory) {
                    fullFileName = TryMoveFileToArtistDirectory(fullFileName, artistFromFile, titleFromFile, errorAction);
                }

                var newComposition = DBAccess.AddComposition(artist, album, compositionName, (TimeSpan)duration, fullFileName, yearFromFile, false, errorAction);

                return newComposition;
            }
            catch (Exception ex)
            {
                _logger?.LogError("MediaStreamer.IO: " + ex.Message);
                errorAction?.Invoke("MediaStreamer.IO: " + ex.Message);
                return null;
            }
        }

        private static NameAndYear ExcludeYearIfExists(string compositionName)
        {
            var result = new NameAndYear();
            var cName = compositionName.Trim();
            int cLength = cName.Length - 1;
            if ( cLength != 0 && cLength > 6 && HasYear(cName) )
            {
                var year = compositionName.TrimEnd().Substring(compositionName.Length - 6, 6);
                var noYearName = compositionName.TrimEnd().Substring(0, compositionName.Length - year.Length);
                if (year[0] == '(' && year[year.Length - 1] == ')')
                    result.Name = noYearName.Trim();
                result.Year = year.Trim('(').Trim(')');
            } else {
                result.Name = compositionName;
                result.Year = "";
            }
            return result;
        }

        private static bool HasYear(string cName)
        {
            if (!string.IsNullOrEmpty(cName))
            {
                const int charsInYear = 4;
                var noBrackets = cName.Trim().Replace(")", "").Replace("(", "");
                var last4Char = noBrackets.Substring(noBrackets.Length - charsInYear, charsInYear);
                if (AreAllDigits(last4Char))
                    return true;
            }
            return false;
        }

        private static bool AreAllDigits(string possibleDigits)
        {
            for(int i = 0; i < possibleDigits.Length; i++)
                if(!char.IsDigit(possibleDigits[i]))
                    return false;
            return true;
        }

        public string ResolveArtistTitleConflicts(string fileName, string titleFromMetaD, string artistFromMetaD, ref string artistName, ref string compositionName)
        {
            string divider;
            if (fileName.Contains(divider = "-") || fileName.Contains(divider = "—"))
            {
                int firstPartLength = fileName.IndexOf(divider);
                int secondPartStart = fileName.IndexOf(divider) + 1;

                artistName = fileName.Substring(0, firstPartLength);
                compositionName = fileName.Substring(secondPartStart);
            }
            else
            {
                divider = null;
            }

            if (artistFromMetaD == null || artistFromMetaD.ToLower() == "unknown")
            {
                if (divider != null)
                {
                    artistName = artistName.TrimStart(divider.ToCharArray()[0]).TrimStart(' ');
                    artistName = artistName.TrimEnd(divider.ToCharArray()[0]).TrimEnd(' ');
                }
                else
                {
                    artistName = "unknown";
                }
            }
            else
            {
                artistName = artistFromMetaD;
            }

            if (titleFromMetaD == null || titleFromMetaD.ToLower() == "unknown")
            {
                if (divider != null)
                {
                    compositionName = compositionName.TrimStart(divider.ToCharArray()[0]).TrimStart(' ');
                    compositionName = compositionName.TrimEnd(divider.ToCharArray()[0]).TrimEnd(' ');
                }
                else
                {
                    compositionName = fileName;
                }
            }
            else
            {
                compositionName = titleFromMetaD;
            }

            return divider;
        }

        public async Task<string> GetOpenedDatabasePathAsync(Action<string> errorAction = null)
        {
            try
            {
                Plugin.FilePicker.Abstractions.FileData fileData = await Plugin.FilePicker.CrossFilePicker.Current.PickFile();
                if (fileData == null)
                    return ""; // user canceled file picking
                string fileName = fileData.FilePath;

                //string contents = System.Text.Encoding.UTF8.GetString(fileData.DataArray);
                //_logger?.LogInfo("File data: " + contents);
                //_logger?.LogInfo("File name chosen: " + fileName);
                return fileName;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex.Message);
                _logger?.LogError("Exception choosing file: " + ex.ToString());
                return ""; 
            }
        }

        public string ExcludeExtension(string source)
        {
            var dotIndex = -1;
            int extensionWithDotLength = 0;
            for (int i = source.Length - 1; i >= 0; i--) {
                extensionWithDotLength++;
                if (source[i] == '.') {
                    if (extensionWithDotLength < 2)
                        return source;
                    dotIndex = i;
                    break;
                }
                if (
                    (source[i] < 'a' || source[i] > 'z') &&
                    (source[i] > 'Z' || source[i] < 'A') &&
                    (source[i] < '0' || source[i] > '9')
                )
                    return source;
            }

            int withoutExtensionLength = source.Length - extensionWithDotLength;

            if (extensionWithDotLength > 5)
                return source;
            // Because there is probably no audio extension with more then 5 symbols at the moment

            return source.Substring(0, withoutExtensionLength);
        }

        public async Task<string> OpenAudioFileCrossPlatform(Action<string> errorAction = null)
        {
            try
            {
                var fileData = Plugin.FilePicker.CrossFilePicker.Current.PickFile();
                if (fileData == null)
                    return null; // user canceled file picking
                fileData.Wait(); // await is not awailable in .Net Framework 4.0 IDEs
                return fileData.Result.FilePath;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex.Message);
                return null;
            }
        }

        public List<string> OpenAudioFilesCrossPlatform(Action<string> errorAction = null)
        {
            try {
                var fileData = Plugin.FilePicker.CrossFilePicker.Current.PickFile();
                if (fileData == null)
                    return null; // user canceled file picking
                fileData.Wait(); // await is not awailable in .Net Framework 4.0 IDEs
                return fileData.Result.FileNames;
            }
            catch (NullReferenceException nre) {
                _logger?.LogTrace("User canceled file picking: " + nre.Message);
                return null;
            }
            catch (Exception ex) {
                _logger?.LogError("MediaStreamer.IO: " + ex.Message);
                return null;
            }
        }
    }
}
