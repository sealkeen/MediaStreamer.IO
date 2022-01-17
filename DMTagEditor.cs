using System;
using System.Collections.Generic;
using System.Linq;
using DMEntitiesDataLibrary;
using DMFileTypes;

namespace DMFileManipulator
{
    public class DMTagEditor
    {
        private List<TagLib.Tag> _tagsv2 = null;
        private List<Composition> _compositions = null;
        private List<TagLib.File> _files = null;
        private Action<string> _statusSetter = null;

        public DMTagEditor(List<TagLib.File> tagFiles, List<Composition> compositions, Action<string> statusSetter = null)
        {
            _statusSetter = statusSetter;
            if (tagFiles == null || compositions == null) {
                SetCurrentStatus("An error occured. The requested composition wasn't found.");
                return;
            }

            _files = tagFiles;
            _compositions = compositions;
            _tagsv2 = new List<TagLib.Tag>();

            for (int compi = 0; compi < compositions.Count; compi++) {
                _tagsv2.Add(tagFiles[compi].GetTag(TagLib.TagTypes.Id3v2));
            }
        }
        public static void AddTitleToCompositionsSourceFile(string title, Composition composition, Action<string> errorAction = null)
        {
            try
            {
                TagLib.File tagFile = TagLib.File.Create(composition.FilePath);
                var tagv2 = tagFile.GetTag(TagLib.TagTypes.Id3v2);
                //check if the title isn't null
                if (!string.IsNullOrEmpty(title))
                {
                    string titleFromFile = tagv2.Title;
                    if (titleFromFile == null || !title.ToLower().Contains(titleFromFile.ToLower()))
                    {
                        tagv2.Title = title;
                        tagFile.Save();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }
        public static void AddArtistToCompositionsSourceFile(string artist, Composition composition, Action<string> errorAction = null)
        {
            try
            {
                TagLib.File tagFile = TagLib.File.Create(composition.FilePath);
                var tagv2 = tagFile.GetTag(TagLib.TagTypes.Id3v2);
                //check if the artist isn't null
                if (!string.IsNullOrEmpty(artist))
                {
                    string[] artistsFromFile = tagv2.Performers;

                    if (artistsFromFile == null || artistsFromFile.Count() == 0
                        || !artist.ToLower().Contains(artistsFromFile[0].ToLower()))
                    {
                        tagv2.Performers = AddNewArtist(artist, artistsFromFile);
                        tagFile.Save();
                    }

                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
            }
        }

        public void AddYear(uint year)
        {
            try
            {
                for (int i = 0; i < _tagsv2.Count; i++)
                {
                    //check if the year isn't null
                    if (year != uint.MaxValue)
                    {
                        uint yearFromFile = _tagsv2[i].Year;

                        if (yearFromFile != year)
                        {
                            _tagsv2[i].Year = year;
                            _files[i].Save();

                            if (_compositions[i].Album != null)
                            {
                                _compositions[i].Album.Year = year;
                                DBAccess.dB.SaveChanges();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
                SetCurrentStatus(ex.Message);
            }
        }

        public void AddTitle(string title, Action<string> setStatus = null)
        {
            try
            { //check if the title isn't null
                if (!string.IsNullOrEmpty(title))
                {
                    for (int i = 0; i < _compositions.Count; i++)
                    {
                        string titleFromFile = _tagsv2[i].Title;
                        if (titleFromFile == null ||
                            !title.ToLower().Contains(title))
                        {
                            _tagsv2[i].Title = title;
                            _files[i].Save();

                            if (_compositions[i] != null)
                            {
                                _compositions[i].CompositionName = title;
                                DBAccess.dB.SaveChanges();
                                if (setStatus != null)
                                    setStatus($"Title Successfully Changed: {title}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
                SetCurrentStatus(ex.Message);
            }
        }

        public void AddAlbum(string artistName, string album,
            long? year = null, string label = null, string type = null)
        {
            try
            {
                for (int i = 0; i < _compositions.Count; i++)
                {
                    //step in if the album isn't null
                    if (!string.IsNullOrEmpty(album))
                    {
                        string albumFromFile = _tagsv2[i].Album;
                        if (albumFromFile == null || !album.ToLower().Contains(albumFromFile.ToLower()))
                        {
                            _tagsv2[i].Album = album;
                            _files[i].Save();
                            if (_compositions[i] != null)
                            {
                                if (artistName != null)
                                {
                                    Artist foundArtist = DBAccess.GetFirstArtistIfExists(artistName);
                                    if (foundArtist == null)
                                    {
                                        foundArtist = DBAccess.AddArtist(artistName);
                                    }
                                    Album foundAlbum = DBAccess.GetFirstAlbumIfExists(artistName, album);
                                    if (foundAlbum == null)
                                    {
                                        foundAlbum = DBAccess.AddAlbum(foundArtist.ArtistName, album, year, label, type);
                                    }
                                    _compositions[i].Album = foundAlbum;
                                    _compositions[i].AlbumID = foundAlbum.AlbumID;
                                    DBAccess.dB.SaveChanges();
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
                SetCurrentStatus(ex.Message);
            }
        }
        public void RemoveArtists(string artist)
        {
            //_tagsv2[i].Performers
        }
        public void ChangeArtist(string artist)
        {
            for (int i = 0; i < _compositions.Count; i++)
            {
                try
                {
                    _files[i].GetTag(TagLib.TagTypes.Id3v2).Performers = GetNewArtistList(artist);
                    _files[i].Save();
                    if (_compositions[i] != null)
                    {
                        Artist foundArtist = DBAccess.AddArtist(artist);

                        if (foundArtist != null)
                        {
                            _compositions[i].Artist = foundArtist;
                            _compositions[i].ArtistID = foundArtist.ArtistID;
                            DBAccess.dB.SaveChanges();
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex.Message);
                    SetCurrentStatus(ex.Message);
                }
            }
        }
        public void AddArtist(string artist, Composition composition)
        {
            try
            {
                //check if the artist isn't null
                if (!string.IsNullOrEmpty(artist))
                {
                    for (int i = 0; i < _compositions.Count; i++)
                    {
                        string[] artistsFromFile = _tagsv2[i].Performers;
                        //if (artistsFromFile == null || artistsFromFile.Count() == 0) {
                        //continue
                        //} else {
                        if (artistsFromFile == null || artistsFromFile.Count() == 0
                            || !artist.ToLower().Contains(artistsFromFile[0].ToLower())
                            )
                        {
                            _tagsv2[i].Performers = AddNewArtist(artist, artistsFromFile);
                            _files[i].Save();
                            if (_compositions[i] != null)
                            {
                                Artist foundArtist = DBAccess.GetFirstArtistIfExists(artist);
                                if (foundArtist != null)
                                {
                                    _compositions[i].Artist = foundArtist;
                                    _compositions[i].ArtistID = foundArtist.ArtistID;
                                    DBAccess.dB.SaveChanges();
                                }
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine(ex.Message);
                SetCurrentStatus(ex.Message);
            }
        }
        public static string[] GetNewArtistList(string artist)
        {
            return new string[] { artist };
        }
        public static string[] AddNewArtist(string artist, string[] artistsFromFile)
        {
            try
            {
                string[] newArtists = new string[artistsFromFile.Length + 1];
                newArtists[0] = artist;
                artistsFromFile.CopyTo(newArtists, 1);
                return newArtists;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
                return null;
            }
        }
        public void SetCurrentStatus(string status, Action<string> statusSetter = null)
        {
            try
            {
                if (statusSetter != null)
                    statusSetter(status);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message);
                SetCurrentStatus(ex.Message);
            }
        }
    }
}
