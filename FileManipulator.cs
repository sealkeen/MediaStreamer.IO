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
    public class FileManipulator
    {
        //TODO: Split long methods into several parts
        //TODO: Implement "Play several songs" cross-platformely
        public IDBRepository DBAccess { get; set; }
        public string WinAmpDir { get; set; }
        protected ILogger _logger;
        public FileManipulator(IDBRepository iDBAccess, ILogger logger)
        {
            DBAccess = iDBAccess;
            _logger = logger;
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
                if (fileName == null || !System.IO.File.Exists(fileName))
                {
                    return null;
                }

                _logger?.LogInfo($"Creating TagLib");
                var tfile = TagLib.File.Create($"{fileName}");

                _logger?.LogInfo($"Fetching data from file...");
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
                        _logger?.LogError($"The file does not have enough information to add a song.");
                        _logger?.LogError($"The file does not have enough information to add a song.");
                        return null;
                    } 
                } else {
                    _logger?.LogInfo($"Adding Artist...");
                    var artist = DBAccess.AddArtist(artistFromFile, errorAction);
                    _logger?.LogInfo($"Adding Genre...");
                    var genre = DBAccess.AddGenreToArtist(artist, genreFromFile); //error
                    _logger?.LogInfo($"Adding Album...");
                    var album = DBAccess.AddAlbum(artist.ArtistName, albumFromFile, yearFromFile, null, null, errorAction);
                    _logger?.LogInfo($"Adding Composition...");
                    newComposition = DBAccess.AddComposition(artist, album, titleFromFile, duration, fileName, null, false, errorAction);
                    _logger?.LogInfo($"Composition is not null: {newComposition != null}, FilePath ok: {newComposition?.FilePath != null} ");
                }
                DBAccess.DB.SaveChanges();
                DMTagEditor.AddArtistToCompositionsSourceFile(newComposition.Artist.ArtistName, newComposition, errorAction);
                DMTagEditor.AddTitleToCompositionsSourceFile(newComposition.CompositionName, newComposition, errorAction);
                return newComposition;
            }
            catch (Exception ex)
            {
                _logger?.LogError("MediaStreamer.IO: " + ex.Message);
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

                compositionName = ExcludeExtension(compositionName);

                if (artistName.Length < 1 || compositionName.Length < 1)
                    return null;

                _logger?.LogInfo($"Adding Artist...");
                var artist = DBAccess.AddArtist(artistName);
                _logger?.LogInfo($"Adding Genre...");
                var genre = DBAccess.AddGenreToArtist(artist, genreFromFile);
                if (artist.ArtistName == null || artist.ArtistName == string.Empty)
                    return null;
                _logger?.LogInfo($"Adding Album...");
                var album = DBAccess.AddAlbum(artist, genre, albumFromFile, null, null, yearFromFile);
                var newComposition = DBAccess.AddComposition(artist, album, compositionName, (TimeSpan)duration, fullFileName);

                return newComposition;
            }
            catch (Exception ex)
            {
                _logger?.LogError("MediaStreamer.IO: " + ex.Message);
                return null;
            }
        }

        private static string ResolveArtistTitleConflicts(string fileName, string titleFromMetaD, string artistFromMetaD, ref string artistName, ref string compositionName)
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
                return fileName;

                string contents = System.Text.Encoding.UTF8.GetString(fileData.DataArray);
                _logger?.LogInfo("File name chosen: " + fileName);
                _logger?.LogInfo("File data: " + contents);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex.Message);
                _logger?.LogError("Exception choosing file: " + ex.ToString());
                return ""; 
            }
        }

        public string ExcludeExtension(string withPossibleExtension)
        {
            var dotIndex = -1;
            int extensionWithDotLength = 0;
            for (int i = withPossibleExtension.Length - 1; i >= 0; i--)
            {
                extensionWithDotLength++;
                if (withPossibleExtension[i] == '.')
                {
                    if (extensionWithDotLength < 2)
                        return withPossibleExtension;
                    dotIndex = i;
                    break;
                }
                if (
                    (withPossibleExtension[i] < 'a' ||
                    withPossibleExtension[i] > 'z')
                    &&
                    (withPossibleExtension[i] > 'Z' ||
                    withPossibleExtension[i] < 'A')
                    &&
                    (withPossibleExtension[i] < '0' ||
                    withPossibleExtension[i] > '9')
                )
                    return withPossibleExtension;
            }

            int withoutExtensionLength = withPossibleExtension.Length - extensionWithDotLength;

            if (extensionWithDotLength > 5)
                return withPossibleExtension;
            //because there is no audio extension with more then 4 symbols

            return withPossibleExtension.Substring(0, withoutExtensionLength);
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
                // User canceled file picking
                return null;
            }
            catch (Exception ex) {
                _logger?.LogError("MediaStreamer.IO: " + ex.Message);
                return null;
            }
        }

        public void PlaySeveralSongs(System.Collections.IList selectedItems, Type itemType, Action<string> errorAction = null)
        {
            //try
            //{
            //    if (selectedItems.Count <= 0)
            //    {
            //        return;
            //    }

            //    if (!WinAmpDir.FileExistsOrValidURL())
            //    {
            //        WinAmpDir = @"C:\Program Files (x86)\Winamp\winamp.exe";
            //        OpenFileDialog openFileDialog = new OpenFileDialog();
            //        if (openFileDialog.ShowDialog() == DialogResult.OK)
            //        {
            //            if(openFileDialog.FileName.FileExistsOrValidURL())
            //                WinAmpDir = openFileDialog.FileName;
            //        }
            //    }

            //    for (int index = 0; index < selectedItems.Count; index++)
            //    {
            //        string path = "";
            //        if (itemType.Name == "Composition" || itemType == null)
            //        {
            //            var cmp = (Composition)selectedItems[index];
            //            path = cmp.FilePath;
            //        }
            //        else
            //        {
            //            if (itemType.Name == "ListenedComposition")
            //            {
            //                var cmp = (ListenedComposition)selectedItems[index];
            //                path = cmp.Composition.FilePath;
            //                DBAccess.AddNewListenedComposition(cmp.Composition, cmp.User);
            //            }
            //            else
            //            {
            //                throw new ArgumentException("Unknown item type of a song collection passed as an argument.");
            //            }
            //        }

            //        Process.Start($"{WinAmpDir}", $"\"{path}\"");
            //    }
            //}
            //catch (Exception ex)
            //{
            //    _logger?.LogError(ex.Message);
            //}
        }
    }
}
