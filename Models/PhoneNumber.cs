using System.ComponentModel;

namespace VcfEditor.Models
{
    public class PhoneNumber : INotifyPropertyChanged
    {
        private string? _number;
        private PhoneNumberType _type;
        private int? _androidRawType;

        public PhoneNumber()
        {
            Type = PhoneNumberType.CELL;
        }

        public PhoneNumber(string number, PhoneNumberType type = PhoneNumberType.CELL)
        {
            Number = number;
            Type = type;
        }

        public string? Number
        {
            get => _number;
            set
            {
                _number = value;
                OnPropertyChanged(nameof(Number));
            }
        }

        public PhoneNumberType Type
        {
            get => _type;
            set
            {
                _type = value;
                OnPropertyChanged(nameof(Type));
                OnPropertyChanged(nameof(TypeDisplayName));
            }
        }

        public string TypeDisplayName
        {
            get
            {
                if (Type == PhoneNumberType.XCustom && !string.IsNullOrWhiteSpace(CustomLabel))
                    return CustomLabel;

                return Type switch
                {
                    PhoneNumberType.CELL => "Mobile",
                    PhoneNumberType.HOME => "Home",
                    PhoneNumberType.WORK => "Work",
                    PhoneNumberType.XMobile => "Mobile",
                    PhoneNumberType.XWork => "Work",
                    PhoneNumberType.XHome => "Home",
                    PhoneNumberType.XOther => "Other",
                    PhoneNumberType.XCustom => "Custom",
                    _ => "Other"
                };
            }
        }

        public int? AndroidRawType
        {
            get => _androidRawType;
            set
            {
                _androidRawType = value;
                OnPropertyChanged(nameof(AndroidRawType));
            }
        }
        // phone numbers. Previously DtoMapper.ToPhoneDto only mapped the first label from
        // the Android type integer, losing the custom label entirely on round-trip.
        private string? _customLabel;
        public string? CustomLabel
        {
            get => _customLabel;
            set
            {
                _customLabel = value;
                OnPropertyChanged(nameof(CustomLabel));
                OnPropertyChanged(nameof(TypeDisplayName));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum PhoneNumberType
    {
        CELL,
        HOME,
        WORK,
        XMobile,
        XWork,
        XHome,
        XOther,
        XCustom
    }
}