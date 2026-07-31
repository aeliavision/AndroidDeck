using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Collections.Specialized;

namespace VcfEditor.Models
{
    /// <summary>
    /// Indicates where a contact was loaded from.
    /// </summary>
    public enum ContactSource
    {
        LocalVcf,
        AndroidPhone
    }

    public class Contact : INotifyPropertyChanged
    {
        private string? _firstName;
        private string? _lastName;
        private string? _middleName;
        private string? _prefix;
        private string? _suffix;
        private string? _fullName;
        private string? _organization;
        private string? _title;
        private string? _email;
        private ObservableCollection<string> _emails = null!;
        private ObservableCollection<PhoneNumber> _phoneNumbers = null!;

        // Android integration fields
        private string? _androidId;
        private string? _accountName;
        private string? _accountType;
        private bool _isReadOnly;
        private ContactSource _source = ContactSource.LocalVcf;
        private string? _etag;

        public Contact()
        {
            PhoneNumbers = new ObservableCollection<PhoneNumber>();
            Emails = new ObservableCollection<string>();
        }

        public string? FirstName
        {
            get => _firstName;
            set
            {
                _firstName = value;
                OnPropertyChanged(nameof(FirstName));
                UpdateFullName();
            }
        }

        public string? LastName
        {
            get => _lastName;
            set
            {
                _lastName = value;
                OnPropertyChanged(nameof(LastName));
                UpdateFullName();
            }
        }

        public string? MiddleName
        {
            get => _middleName;
            set
            {
                _middleName = value;
                OnPropertyChanged(nameof(MiddleName));
                UpdateFullName();
            }
        }

        public string? Prefix
        {
            get => _prefix;
            set
            {
                _prefix = value;
                OnPropertyChanged(nameof(Prefix));
                UpdateFullName();
            }
        }

        public string? Suffix
        {
            get => _suffix;
            set
            {
                _suffix = value;
                OnPropertyChanged(nameof(Suffix));
                UpdateFullName();
            }
        }

        public string? FullName
        {
            get => _fullName;
            set
            {
                // UpdateFullName() does not silently overwrite a value that was provided
                // by the server/parser (e.g. a nickname, Kanji reading, or custom display
                // name that differs from the auto-computed "Prefix FirstName … LastName").
                _fullNameExplicitlySet = !string.IsNullOrWhiteSpace(value);
                _fullName = value;
                OnPropertyChanged(nameof(FullName));
            }
        }
        // assigned FullName. Reset to false whenever name-part properties are first set
        // so that a brand-new contact still gets auto-computed display names.
        private bool _fullNameExplicitlySet;

        public string? Organization
        {
            get => _organization;
            set
            {
                _organization = value;
                OnPropertyChanged(nameof(Organization));
            }
        }

        public string? Title
        {
            get => _title;
            set
            {
                _title = value;
                OnPropertyChanged(nameof(Title));
            }
        }

        /// <summary>
        /// SER-02 FIX: Full multi-value email collection. Replaces the single Email string
        /// that silently dropped all but the first email address from the Android contact.
        /// </summary>
        public ObservableCollection<string> Emails
        {
            get => _emails;
            set
            {
                if (_emails != null)
                    _emails.CollectionChanged -= Emails_CollectionChanged;
                _emails = value;
                if (_emails != null)
                    _emails.CollectionChanged += Emails_CollectionChanged;
                OnPropertyChanged(nameof(Emails));
                OnPropertyChanged(nameof(Email));
            }
        }

        private void Emails_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(Email));
        }

        /// <summary>
        /// SER-02 FIX: Backward-compat shim — returns/sets the first email address.
        /// Existing code that reads or writes Contact.Email continues to work; it simply
        /// operates on Emails[0]. New code should use the Emails collection directly.
        /// </summary>
        public string? Email
        {
            get => _emails?.Count > 0 ? _emails[0] : _email;
            set
            {
                _email = value;
                // Keep Emails[0] in sync with the legacy setter.
                if (_emails != null)
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        if (_emails.Count > 0) _emails.RemoveAt(0);
                    }
                    else if (_emails.Count == 0)
                        _emails.Insert(0, value);
                    else
                        _emails[0] = value;
                }
                OnPropertyChanged(nameof(Email));
            }
        }

        public ObservableCollection<PhoneNumber> PhoneNumbers
        {
            get => _phoneNumbers;
            set
            {
                if (_phoneNumbers != null)
                {
                    _phoneNumbers.CollectionChanged -= PhoneNumbers_CollectionChanged;
                }

                _phoneNumbers = value;

                if (_phoneNumbers != null)
                {
                    _phoneNumbers.CollectionChanged += PhoneNumbers_CollectionChanged;
                }

                OnPropertyChanged(nameof(PhoneNumbers));
                OnPropertyChanged(nameof(PrimaryPhoneNumber));
            }
        }

        private void PhoneNumbers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(PrimaryPhoneNumber));
        }

        public string? PrimaryPhoneNumber => PhoneNumbers.FirstOrDefault()?.Number;

        // --- Android integration properties ---

        /// <summary>Stable contact ID from the Android Contacts Provider.</summary>
        public string? AndroidId
        {
            get => _androidId;
            set { _androidId = value; OnPropertyChanged(nameof(AndroidId)); }
        }

        /// <summary>Account name this contact belongs to (e.g. "user@gmail.com").</summary>
        public string? AccountName
        {
            get => _accountName;
            set { _accountName = value; OnPropertyChanged(nameof(AccountName)); }
        }

        /// <summary>Account type (e.g. "com.google").</summary>
        public string? AccountType
        {
            get => _accountType;
            set { _accountType = value; OnPropertyChanged(nameof(AccountType)); }
        }

        /// <summary>True if the contact belongs to a read-only account.</summary>
        public bool IsReadOnly
        {
            get => _isReadOnly;
            set { _isReadOnly = value; OnPropertyChanged(nameof(IsReadOnly)); }
        }

        /// <summary>Where this contact was loaded from.</summary>
        public ContactSource Source
        {
            get => _source;
            set { _source = value; OnPropertyChanged(nameof(Source)); }
        }

        /// <summary>Server-side version/etag for concurrency control.</summary>
        public string? Etag
        {
            get => _etag;
            set { _etag = value; OnPropertyChanged(nameof(Etag)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Creates a true deep copy of this contact.
        /// same ObservableCollection&lt;PhoneNumber&gt; instance, so edits to the clone's phone
        /// list mutated the original contact's list before the user confirmed changes.
        /// </summary>
        public Contact Clone()
        {
            var clone = new Contact
            {
                _firstName = _firstName,
                _lastName = _lastName,
                _middleName = _middleName,
                _prefix = _prefix,
                _suffix = _suffix,
                _fullName = _fullName,
                _fullNameExplicitlySet = _fullNameExplicitlySet,
                _organization = _organization,
                _title = _title,
                _email = _email,
                _androidId = _androidId,
                _accountName = _accountName,
                _accountType = _accountType,
                _isReadOnly = _isReadOnly,
                _source = _source,
                _etag = _etag
            };
            // edits to the clone's list do not affect the original.
            clone.PhoneNumbers = new ObservableCollection<PhoneNumber>();
            foreach (var phone in PhoneNumbers)
            {
                clone.PhoneNumbers.Add(new PhoneNumber(phone.Number ?? string.Empty, phone.Type)
                {
                    AndroidRawType = phone.AndroidRawType
                });
            }

            // SER-02 FIX: Deep-copy the Emails collection.
            clone.Emails = new ObservableCollection<string>(_emails ?? new ObservableCollection<string>());

            return clone;
        }

        public void UpdateFrom(Contact other)
        {
            if (other == null) return;

            FirstName = other.FirstName;
            LastName = other.LastName;
            MiddleName = other.MiddleName;
            Prefix = other.Prefix;
            Suffix = other.Suffix;
            
            if (!string.IsNullOrWhiteSpace(other.FullName))
            {
                FullName = other.FullName;
            }
            
            Organization = other.Organization;
            Title = other.Title;
            // SER-02 FIX: Merge the full Emails collection, not just the first address.
            Emails.Clear();
            foreach (var e in other.Emails) Emails.Add(e);
            AccountName = other.AccountName;
            AccountType = other.AccountType;
            IsReadOnly = other.IsReadOnly;
            Source = other.Source;
            Etag = other.Etag;

            // Merge phone numbers
            if (other.PhoneNumbers != null)
            {
                PhoneNumbers.Clear();
                foreach (var phone in other.PhoneNumbers)
                {
                    PhoneNumbers.Add(phone);
                }
                OnPropertyChanged(nameof(PrimaryPhoneNumber));
            }
        }

        private void UpdateFullName()
        {
            // a server DTO or from the FN property in a VCF file). Only auto-compute when
            // no explicit value has been provided.
            if (_fullNameExplicitlySet) return;
            var parts = new[] { Prefix, FirstName, MiddleName, LastName, Suffix }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToArray();

            _fullName = string.Join(" ", parts);
            OnPropertyChanged(nameof(FullName));
        }
    }
}