using System;
using System.Globalization;
using System.IO;

namespace MainGameBlankMapAdd.Media.Images
{
    internal sealed class SlideshowDateFolder
    {
        private string _root;
        private string _selected = string.Empty;
        private DateTime _date;
        private bool _foundToday;
        private double _nextSearch;

        internal string Resolve(string root, DateTime today, double now,
            Func<string, bool> exists, Func<string, string> findInitialFolder)
        {
            today = today.Date;
            bool rootChanged = !string.Equals(root, _root, StringComparison.OrdinalIgnoreCase);
            if (rootChanged)
            {
                _root = root;
                _selected = string.Empty;
            }
            if (rootChanged || today != _date)
            {
                _date = today;
                _foundToday = false;
                _nextSearch = now;
            }
            if (_foundToday || now < _nextSearch)
                return _selected;

            _nextSearch = now + 5d;
            string target = Path.Combine(root, today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (exists(target))
            {
                _selected = target;
                _foundToday = true;
            }
            else if (rootChanged)
            {
                // Keep an existing image folder usable until today's folder is created.
                _selected = findInitialFolder(root) ?? string.Empty;
            }
            return _selected;
        }
    }
}
