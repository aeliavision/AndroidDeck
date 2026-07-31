# VCF Contact Editor - Complete User Guide

## 🎯 Overview

The **VCF Contact Editor** is a professional Windows application designed for managing VCF (vCard) contact files. Built with modern C# and WPF technology, it provides a user-friendly interface for viewing, editing, and organizing your contacts with powerful features and intuitive controls.

### ✨ Key Features

- 📁 **File Management** - Open, save, and manage VCF contact files
- 👥 **Contact Operations** - Add, edit, delete, and organize contacts
- 🔍 **Advanced Search** - Find contacts by name, phone, organization, or email
- 📞 **Phone Management** - Full CRUD operations for phone numbers
- ⚡ **Fast & Responsive** - Asynchronous operations for smooth performance
- 🎨 **Modern Interface** - Clean, professional design
- ⌨️ **Keyboard Support** - Full keyboard navigation and shortcuts
- 🎯 **Drag & Drop** - Modern file handling
- 🌍 **Internationalization Ready** - Support for multiple languages
- 📊 **Professional Features** - Enterprise-grade reliability

---

## 🚀 Quick Start

### Installation

1. **Download** the VCF Contact Editor application
2. **Extract** to any folder on your computer
3. **Run** `VcfEditor.exe` (no installation required)
4. **Ready to use!** 🎉

### System Requirements

- **Operating System**: Windows 10/11 (64-bit recommended)
- **Framework**: .NET 8.0 Runtime
- **Memory**: 100 MB RAM minimum
- **Storage**: 50 MB free space
- **Screen**: 1024x768 minimum resolution

---

## 📖 How to Use

### Opening a VCF File

1. **Launch** the application
2. **Click "Open File"** in the toolbar or press `Ctrl+O`
3. **Select your VCF file** and click "Open"
4. **Contacts appear** in the list automatically

**Alternative methods:**
- **Drag and drop** VCF files onto the application window
- **Double-click VCF files** associated with the application

### Viewing Contacts

- **Contact List** shows all your contacts with name, phone, organization, and email
- **Click any contact** to view details in the right panel
- **Use search box** to filter contacts instantly
- **Sort by columns** by clicking column headers

### Adding New Contacts

1. **Click "Add Contact"** button or press `Ctrl+N`
2. **Fill in contact details** in the dialog:
   - Personal information (name, prefix, suffix)
   - Professional details (organization, title, email)
   - Phone numbers with types
3. **Click "Save"** to add the contact

### Editing Contacts

**Method 1: Using Toolbar**
1. Select a contact from the list
2. Click "Edit Contact" button or press `Ctrl+E`
3. Modify details in the dialog
4. Click "Save Changes"

**Method 2: Double-Click (Fastest)**
1. **Double-click any contact** in the list
2. Edit details in the dialog
3. Click "Save Changes"

**Method 3: Enter Key**
1. Select a contact
2. **Press Enter** on your keyboard
3. Edit and save

### Deleting Contacts

1. **Select contact(s)** from the list
2. **Click "Delete Contact"** or press `Delete` key
3. **Confirm deletion** in the dialog

**Tip:** Hold `Ctrl` and click to select multiple contacts for batch deletion.

### Managing Phone Numbers

Each contact can have multiple phone numbers with different types:

1. **Add Phone**: Click the ➕ button in the Phone Numbers section
2. **Edit Phone**: Select a phone number and click the ✏️ button
3. **Delete Phone**: Select a phone number and click the ➖ button

**Phone Types Available:**
- 📱 **Mobile** - Cell phones
- 🏠 **Home** - Home phone numbers
- 💼 **Work** - Business phone numbers
- 📞 **Other** - Additional numbers

---

## ⌨️ Complete Keyboard Support

### File Operations
| Shortcut | Action | Description |
|----------|--------|-------------|
| `Ctrl+O` | Open File | Open a VCF file |
| `Ctrl+S` | Save File | Save current file |
| `Ctrl+Shift+S` | Save As | Save to new file |
| `Alt+F4` | Exit | Close application |

### Contact Management
| Shortcut | Action | Description |
|----------|--------|-------------|
| `Ctrl+N` | New Contact | Add new contact |
| `Ctrl+E` | Edit Contact | Edit selected contact |
| `Delete` | Delete Contact | Delete selected contact(s) |
| `Enter` | Quick Edit | Edit selected contact (fastest method) |
| `Ctrl+F` | Focus Search | Jump to search box |

### Navigation & Selection
| Shortcut | Action | Description |
|----------|--------|-------------|
| `↑/↓` | Navigate List | Move up/down in contact list |
| `Ctrl+A` | Select All | Select all contacts |
| `Ctrl+Click` | Multi-Select | Select multiple contacts |
| `Tab` | Next Field | Move to next UI element |
| `Shift+Tab` | Previous Field | Move to previous UI element |

### Advanced Shortcuts
| Shortcut | Action | Description |
|----------|--------|-------------|
| `F1` | Help | Show help information |
| `Ctrl+Z` | Undo | Undo last action (when available) |
| `Ctrl+Y` | Redo | Redo last undone action |
| `F5` | Refresh | Refresh contact list |

---

## 🔍 Search and Filter

### Search Options

The application provides powerful search capabilities:

1. **Search Box** - Type any text to search
2. **Filter Dropdown** - Choose search scope:
   - **All Fields** - Search across all contact data
   - **Name** - Search in names only
   - **Phone** - Search in phone numbers only
   - **Organization** - Search in organization field only

### Search Tips

- **Partial matches** work (e.g., "john" finds "john.doe@example.com")
- **Case insensitive** - "JOHN" finds "john.doe@example.com"
- **Multiple words** - "john doe" finds contacts with both words
- **Real-time filtering** - Results update as you type

### Advanced Search Examples

| Search For | Finds |
|------------|-------|
| "john" | Any contact containing "john" |
| "john doe" | Contacts with both "john" AND "doe" |
| "gmail" | Contacts with "gmail" in any field |
| "+123" | Phone numbers starting with "+123" |

---

## 📞 Phone Number Management

### Adding Phone Numbers

1. **Select a contact** from the list
2. **Click "➕ Add Phone"** in the Phone Numbers section
3. **Enter phone number** and select type
4. **Click "Save"**

### Editing Phone Numbers

1. **Select a contact**
2. **Click on a phone number** in the list
3. **Click "✏️ Edit Phone"** button
4. **Modify number or type**
5. **Click "Save"**

**Quick Edit:** Double-click any phone number to edit it directly.

### Phone Number Types

- **📱 Mobile** - Cell phones and mobile numbers
- **🏠 Home** - Personal/home phone numbers
- **💼 Work** - Business/office numbers
- **📞 Other** - Additional phone numbers

### Phone Number Validation

The application automatically validates phone numbers:
- ✅ **Minimum 7 digits** required
- ✅ **Maximum 15 digits** allowed
- ✅ **Only digits, spaces, hyphens, parentheses, and + allowed**
- ❌ **Letters and special characters** not permitted

---

## 💾 File Operations

### Supported Formats

- **VCF v2.1** - Standard vCard format
- **UTF-8 encoding** - Supports international characters
- **Multiple contacts** - Handles files with many contacts

### Opening Files

**Method 1: Menu**
- File → Open (Ctrl+O)
- Browse and select VCF file

**Method 2: Drag & Drop**
- Drag VCF file from Explorer
- Drop onto application window

**Method 3: File Association**
- Double-click VCF files in Explorer
- Opens automatically in VCF Editor

### Saving Files

**Method 1: Save**
- File → Save (Ctrl+S)
- Updates current file

**Method 2: Save As**
- File → Save As (Ctrl+Shift+S)
- Save to new location

### File Size Limits

- **Maximum file size**: 50 MB
- **Recommended limit**: 10,000 contacts per file
- **Performance**: Best with 1,000-5,000 contacts

---

## 🎨 User Interface Guide

### Main Window Layout

```
┌─────────────────────────────────────────────────────────┐
│  📁 File  ✏️ Edit  ❓ Help                              │ ← Menu Bar
├─────────────────────────────────────────────────────────┤
│  📁 Open  💾 Save  ➕ Add  ✏️ Edit  🗑️ Delete         │ ← Toolbar
├─────────────────────────────────────────────────────────┤
│  🔍 Search [____________] ▼Filter  Clear              │ ← Search Bar
├─────────────────────────────────────────────────────────┤
│  📋 Contacts List              │ 👤 Contact Details     │ ← Main Area
│  ┌─────────────────────────┐  │ ┌───────────────────┐  │
│  │ 👤 Name    📱 Phone      │  │ │ Name: John Doe    │  │
│  │ 🏢 Org     🏷️ Title      │  │ │ Phone: +1234567890│  │
│  │ ✉️ Email                │  │ │ Email: john@ex.com│  │
│  └─────────────────────────┘  │ └───────────────────┘  │
├─────────────────────────────────────────────────────────┤
│  💡 Tip: Double-click or Enter to edit • Ctrl+Click    │ ← Status Bar
└─────────────────────────────────────────────────────────┘
```

### Contact List Columns

| Column | Description | Sortable |
|--------|-------------|----------|
| 👤 **Name** | Contact's full name | ✅ Yes |
| 📱 **Phone** | Primary phone number | ✅ Yes |
| 🏢 **Organization** | Company or organization | ✅ Yes |
| 🏷️ **Title** | Job title or position | ✅ Yes |
| ✉️ **Email** | Email address | ✅ Yes |

### Status Bar Information

- **Contact count** - Shows total number of contacts
- **Tips and hints** - Helpful usage information
- **Operation status** - Current operation feedback

---

## 🛠️ Advanced Features

### Batch Operations

**Multiple Selection:**
- Hold `Ctrl` and click to select multiple contacts
- Use `Ctrl+A` to select all contacts
- Selected contacts can be deleted together

**Bulk Editing:**
- Select multiple contacts with same organization
- Edit operations affect all selected contacts
- Useful for updating company information

### Data Validation

**Automatic Validation:**
- Phone numbers must be 7-15 digits
- Email format validation
- Required fields enforcement
- Real-time feedback

**Error Messages:**
- Clear, actionable error descriptions
- Suggestions for fixing issues
- Validation on save to prevent bad data

### Performance Features

**Large File Support:**
- Handles thousands of contacts efficiently
- Asynchronous file operations
- Memory-efficient processing
- Progress indicators for long operations

**Responsive UI:**
- Non-blocking file operations
- Smooth scrolling in large lists
- Fast search and filtering
- Optimized for modern hardware

---

## 🔧 Troubleshooting

### Common Issues

#### Application Won't Start
**Solutions:**
- Install .NET 8.0 Runtime from Microsoft
- Check Windows version compatibility
- Run as Administrator if permission issues
- Check antivirus software blocking

#### File Won't Open
**Solutions:**
- Verify file is valid VCF format
- Check file encoding (should be UTF-8)
- Ensure file is not corrupted
- Try opening with a text editor first

#### Contacts Not Saving
**Solutions:**
- Check file write permissions
- Ensure disk space is available
- Verify file is not read-only
- Try "Save As" with a new filename

#### Search Not Working
**Solutions:**
- Check search filter selection
- Verify contact data exists
- Try clearing search and starting over
- Restart application if issues persist

### Getting Help

**Log Files:**
- Located in `%LOCALAPPDATA%/VcfEditor/Logs/`
- Include with support requests
- Shows detailed error information

**Support Contact:**
- Check application logs first
- Provide steps to reproduce issue
- Include sample files if relevant

---

## 📚 Tips and Tricks

### Productivity Tips

1. **Quick Edit** - Double-click any contact to edit instantly
2. **Fast Search** - Use `Ctrl+F` to jump to search box
3. **Bulk Operations** - Select multiple contacts with `Ctrl+Click`
4. **Keyboard Navigation** - Use arrow keys to navigate lists
5. **Recent Files** - Application remembers recently opened files

### Data Management

1. **Regular Backups** - Save important contact files regularly
2. **Validate Data** - Use search to verify data integrity
3. **Organize Contacts** - Use consistent organization names
4. **Phone Number Types** - Properly categorize phone numbers
5. **Export Regularly** - Keep backups of important contacts

### Performance Optimization

1. **File Size** - Keep VCF files under 10,000 contacts for best performance
2. **Search Scope** - Use specific filters instead of "All Fields" for large datasets
3. **Memory Usage** - Close application when not in use for extended periods
4. **Disk Space** - Ensure adequate free space for temporary files

---

## 🔄 Updates and Maintenance

### Checking for Updates

The application will notify you of available updates:
- **Automatic checks** on startup
- **Manual update check** through Help menu
- **Seamless installation** of updates

### Backup Recommendations

**Important Files:**
- Contact VCF files
- Application settings
- Custom configurations

**Backup Frequency:**
- **Daily** - If using for critical business contacts
- **Weekly** - For regular personal use
- **Monthly** - For occasional use

### Data Safety

- **Automatic validation** prevents corrupted data
- **Transaction-like operations** ensure data consistency
- **Error recovery** helps restore from failures
- **Comprehensive logging** tracks all operations

---

## 🎯 Feature Comparison

| Feature | VCF Editor | Basic Text Editor | Online Tools |
|---------|------------|-------------------|--------------|
| **VCF Format Support** | ✅ Full | ❌ None | ⚠️ Limited |
| **Validation** | ✅ Comprehensive | ❌ None | ⚠️ Basic |
| **Search** | ✅ Advanced | ⚠️ Basic | ⚠️ Limited |
| **Batch Operations** | ✅ Yes | ❌ No | ⚠️ Limited |
| **Keyboard Shortcuts** | ✅ Complete | ⚠️ Basic | ❌ None |
| **Drag & Drop** | ✅ Yes | ✅ Yes | ⚠️ Varies |
| **Performance** | ✅ Optimized | ✅ Fast | ⚠️ Varies |
| **Offline Use** | ✅ Yes | ✅ Yes | ❌ No |
| **Privacy** | ✅ Local Only | ✅ Local | ❌ Server Stored |

---

## 📞 Support and Feedback

### Getting Help

**In-App Help:**
- Press `F1` for help
- Check status bar tips
- Review error messages

**External Support:**
- Application logs in `%LOCALAPPDATA%/VcfEditor/Logs/`
- User documentation and guides
- Community forums and resources

### Providing Feedback

**Feature Requests:**
- Use the feedback form in Help menu
- Suggest improvements through support channels
- Participate in user surveys

**Bug Reports:**
- Include detailed steps to reproduce
- Attach relevant log files
- Provide system information

---

## 🎓 Learning Resources

### Documentation
- **User Guide** - This comprehensive guide
- **Technical Documentation** - For developers and advanced users
- **API Reference** - Programming interface documentation

### Video Tutorials
- **Getting Started** - Basic usage tutorial
- **Advanced Features** - Power user features
- **Troubleshooting** - Common issues and solutions

### Online Resources
- **Knowledge Base** - Frequently asked questions
- **Community Forum** - User discussions and tips
- **Video Library** - Tutorial videos and webinars

---

## 🔐 Privacy and Security

### Data Protection
- **Local processing** - All data stays on your computer
- **No data transmission** - Files never sent to external servers
- **Secure storage** - Industry-standard file handling
- **Privacy focused** - No tracking or analytics

### Security Features
- **Input validation** - Prevents malicious data injection
- **Safe file operations** - Protected file handling
- **Error isolation** - Prevents system-wide issues
- **Access control** - Respects Windows file permissions

---

## 🌟 Best Practices

### For Regular Users
1. **Save frequently** - Prevent data loss
2. **Use descriptive names** - Organize contacts effectively
3. **Validate phone numbers** - Ensure correct format
4. **Backup important files** - Regular backup routine
5. **Use search effectively** - Learn advanced search features

### For Business Users
1. **Standardize data entry** - Consistent organization names
2. **Use proper phone types** - Categorize numbers correctly
3. **Regular data validation** - Maintain data quality
4. **Batch operations** - Efficient bulk updates
5. **Integration ready** - Prepare for future integrations

### For Power Users
1. **Keyboard shortcuts** - Maximize efficiency
2. **Advanced search** - Complex filtering operations
3. **Custom configurations** - Optimize for your workflow
4. **Log monitoring** - Track application behavior
5. **Performance tuning** - Optimize for large datasets

---

## 📱 Android Companion (Phone Features)

This application can connect to the **VCFEditor Android Companion** to browse files/gallery and perform encrypted backups.

### Requirements
- Android Companion app installed on your phone
- Desktop and phone on the same network (Wi-Fi) **or** USB with ADB port forwarding
- Grant the required Android permissions when prompted

### Pairing / Connecting
1. Open the Android Companion app and start the server.
2. In the desktop app, open the **Connect** screen and pair with the phone.
3. After pairing, the desktop queries `/api/v2/status` and enables features based on capabilities.

### Capability-driven UI
Some tabs may be disabled depending on the phone/app state:
- **File Browser** requires Android storage permission ("All files access" on Android 11+).
- **Gallery** requires media permissions (`READ_MEDIA_IMAGES` / `READ_MEDIA_VIDEO`).
- **Backup** requires a companion version that supports Phase 5+ (backup endpoints enabled).

If a tab is disabled, the desktop will show a message explaining what permission/update is required.

### Backup & Restore (Encrypted)
The **Backup** tab supports:
- Create backup (contacts / gallery / files)
- Download archive to PC (`.vcfbak`)
- Restore archive back to phone

Notes:
- Archives are encrypted on the phone (AES-GCM) using a persistent device backup key.
- Restore uploads show progress in the desktop UI.

### Troubleshooting Phone Features
- If Backup/Gallery/File Browser is disabled: open the Android Companion app and grant permissions.
- If pairing fails: ensure both devices are on the same subnet, and no VPN/proxy interferes.
- Check logs in `%LOCALAPPDATA%/VcfEditor/Logs/`.

## 📈 What's New

### Latest Version Features
- **Double-click editing** - Fast contact editing
- **Enhanced search** - Improved filtering capabilities
- **Better validation** - More comprehensive data checking
- **Performance improvements** - Faster file operations
- **Modern UI** - Updated interface design

### Upcoming Features
- **Cloud sync** - Synchronize across devices
- **Import/Export options** - CSV, Excel support
- **Advanced filtering** - Custom filter creation
- **Bulk edit operations** - Edit multiple contacts
- **Theme customization** - Personalize appearance

---

## 🎯 Conclusion

The VCF Contact Editor is a powerful, user-friendly application that makes managing VCF contact files simple and efficient. With its comprehensive feature set, professional design, and robust architecture, it's the ideal tool for:

- **📱 Personal contact management**
- **💼 Business contact databases**
- **🏢 Organization address books**
- **📞 Phone number management**
- **🔍 Contact search and organization**

**Start using VCF Contact Editor today and experience the power of professional contact management!** 🚀

---

*For technical support or feature requests, please refer to the application logs or contact the development team.*