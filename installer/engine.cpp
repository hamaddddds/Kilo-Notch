#include "engine.h"
#include "Version.h"
#include <shlobj.h>
#include <shobjidl.h>
#include <setupapi.h>
#include <bcrypt.h>
#include <wrl/client.h>
#include <fstream>
#include <vector>
#include <array>
#include <set>
#include <stdexcept>
#include <algorithm>
#ifdef KILO_PAYLOAD
#include "payload_hashes.h"
#endif

namespace kilo {
using Microsoft::WRL::ComPtr;
constexpr wchar_t ProductKey[] = L"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\KiloNotch";
constexpr wchar_t RunKey[] = L"Software\\Microsoft\\Windows\\CurrentVersion\\Run";
constexpr wchar_t Marker[] = L".kilo-install";
constexpr char Magic[] = "KILO_NOTCH_INSTALL_V1";
const std::array<std::wstring, 3> OwnedFiles{ L"KiloNotch.exe", L"KiloUninstall.exe", Marker };

static void Require(bool success, const char* message) { if (!success) throw std::runtime_error(message); }
static fs::path Known(REFKNOWNFOLDERID id) {
    PWSTR path = nullptr;
    Require(SUCCEEDED(SHGetKnownFolderPath(id, 0, nullptr, &path)), "Windows couldn't locate your user folders.");
    fs::path result(path); CoTaskMemFree(path); return result;
}
Environment CurrentEnvironment() { return { Known(FOLDERID_LocalAppData) / L"Programs" / L"Kilo Notch", Known(FOLDERID_Programs) / L"Kilo Notch.lnk", Known(FOLDERID_Desktop) / L"Kilo Notch.lnk", true }; }
fs::path OwnExecutable() { std::vector<wchar_t> buf(32768); const auto count = GetModuleFileNameW(nullptr, buf.data(), static_cast<DWORD>(buf.size())); Require(count > 0 && count < buf.size(), "Couldn't locate this program."); return fs::path(std::wstring(buf.data(), count)); }

void CheckPath(const fs::path& path) {
    Require(path.is_absolute(), "Installation paths must be absolute.");
    for (auto current = path.lexically_normal(); !current.empty(); current = current.parent_path()) {
        const DWORD attrs = GetFileAttributesW(current.c_str());
        Require(attrs == INVALID_FILE_ATTRIBUTES || !(attrs & FILE_ATTRIBUTE_REPARSE_POINT), "A linked folder was found. Kilo won't change files through a junction or symbolic link.");
        if (current == current.root_path()) break;
    }
}
bool AllowedPayloadName(const std::wstring& name) { return name == L"KiloNotch.exe" || name == L"KiloUninstall.exe"; }
bool IsOwned(const fs::path& root) {
    CheckPath(root); CheckPath(root / Marker);
    std::ifstream file(root / Marker); std::string first; std::getline(file, first); return first == Magic;
}
[[maybe_unused]] static std::wstring Unique() { GUID id{}; Require(SUCCEEDED(CoCreateGuid(&id)), "Couldn't create a temporary folder."); wchar_t buf[40]{}; StringFromGUID2(id, buf, 40); return buf; }
static void RemoveKnown(const fs::path& root) {
    CheckPath(root);
    for (const auto& name : OwnedFiles) { CheckPath(root / name); if (fs::exists(root / name)) fs::remove(root / name); }
    if (fs::exists(root) && fs::is_empty(root)) fs::remove(root);
}

std::wstring Sha256(const fs::path& path) {
    BCRYPT_ALG_HANDLE algorithm{}; BCRYPT_HASH_HANDLE hash{};
    Require(BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) >= 0, "Hash service unavailable.");
    struct Cleanup { BCRYPT_ALG_HANDLE& a; BCRYPT_HASH_HANDLE& h; ~Cleanup() { if (h) BCryptDestroyHash(h); if (a) BCryptCloseAlgorithmProvider(a, 0); } } cleanup{algorithm, hash};
    Require(BCryptCreateHash(algorithm, &hash, nullptr, 0, nullptr, 0, 0) >= 0, "Couldn't verify the package.");
    std::ifstream input(path, std::ios::binary); Require(input.good(), "An installation file is missing.");
    std::array<char, 65536> buffer{};
    while (input) { input.read(buffer.data(), buffer.size()); Require(BCryptHashData(hash, reinterpret_cast<PUCHAR>(buffer.data()), static_cast<ULONG>(input.gcount()), 0) >= 0, "Couldn't hash the package."); }
    Require(input.eof(), "Couldn't read an installation file.");
    std::array<UCHAR, 32> digest{}; Require(BCryptFinishHash(hash, digest.data(), static_cast<ULONG>(digest.size()), 0) >= 0, "Couldn't verify the package.");
    const wchar_t digits[] = L"0123456789abcdef"; std::wstring result;
    for (auto byte : digest) { result += digits[byte >> 4]; result += digits[byte & 15]; } return result;
}

static std::wstring ReadString(const wchar_t* key, const wchar_t* name) {
    DWORD size = 0; if (RegGetValueW(HKEY_CURRENT_USER, key, name, RRF_RT_REG_SZ, nullptr, nullptr, &size) != ERROR_SUCCESS) return {};
    std::vector<wchar_t> text(size / sizeof(wchar_t) + 1);
    if (RegGetValueW(HKEY_CURRENT_USER, key, name, RRF_RT_REG_SZ, nullptr, text.data(), &size) != ERROR_SUCCESS) return {};
    return text.data();
}
static void StringValue(HKEY key, const wchar_t* name, const std::wstring& value) {
    Require(RegSetValueExW(key, name, 0, REG_SZ, reinterpret_cast<const BYTE*>(value.c_str()), static_cast<DWORD>((value.size() + 1) * sizeof(wchar_t))) == ERROR_SUCCESS, "Couldn't register Kilo for your account.");
}
bool StartsWithWindows(const Environment& env) { return ReadString(RunKey, L"Notch") == L"\"" + (env.root / L"KiloNotch.exe").wstring() + L"\""; }
static void Link(const fs::path& file, const fs::path& target) {
    CheckPath(file); fs::create_directories(file.parent_path());
    ComPtr<IShellLinkW> link; Require(SUCCEEDED(CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&link))), "Couldn't create the shortcut.");
    Require(SUCCEEDED(link->SetPath(target.c_str())) && SUCCEEDED(link->SetWorkingDirectory(target.parent_path().c_str())), "Couldn't configure the shortcut.");
    link->SetDescription(L"Kilo Notch"); link->SetIconLocation(target.c_str(), 0);
    ComPtr<IPersistFile> persist; Require(SUCCEEDED(link.As(&persist)) && SUCCEEDED(persist->Save(file.c_str(), TRUE)), "Couldn't save the shortcut.");
}
static void RemoveLink(const fs::path& file, const fs::path& target) {
    CheckPath(file); if (!fs::exists(file)) return;
    ComPtr<IShellLinkW> link; if (FAILED(CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&link)))) return;
    ComPtr<IPersistFile> persist; if (FAILED(link.As(&persist)) || FAILED(persist->Load(file.c_str(), STGM_READ))) return;
    wchar_t destination[MAX_PATH]{};
    if (SUCCEEDED(link->GetPath(destination, MAX_PATH, nullptr, SLGP_RAWPATH))) {
        // Shell links may expand an 8.3 user folder to its long name. Compare file
        // identity as well as spelling, while leaving links to another app alone.
        std::error_code error;
        const bool sameFile = fs::equivalent(fs::path(destination), target, error);
        if (sameFile || _wcsicmp(destination, target.c_str()) == 0) fs::remove(file);
    }
}
[[maybe_unused]] static void Register(const Environment& env, bool desktop, bool startup) {
    HKEY key{}; Require(RegCreateKeyExW(HKEY_CURRENT_USER, ProductKey, 0, nullptr, 0, KEY_WRITE, nullptr, &key, nullptr) == ERROR_SUCCESS, "Couldn't register the installation.");
    try {
        StringValue(key, L"DisplayName", L"Kilo Notch"); StringValue(key, L"DisplayVersion", KiloVersion);
        StringValue(key, L"Publisher", L"Kilo Notch"); StringValue(key, L"InstallLocation", env.root.wstring());
        StringValue(key, L"DisplayIcon", (env.root / L"KiloNotch.exe").wstring());
        StringValue(key, L"UninstallString", L"\"" + (env.root / L"KiloUninstall.exe").wstring() + L"\"");
        StringValue(key, L"URLInfoAbout", L"https://hamaddddds.github.io/Kilo-Notch/");
        const DWORD one = 1;
        Require(RegSetValueExW(key, L"NoModify", 0, REG_DWORD, reinterpret_cast<const BYTE*>(&one), sizeof(one)) == ERROR_SUCCESS && RegSetValueExW(key, L"NoRepair", 0, REG_DWORD, reinterpret_cast<const BYTE*>(&one), sizeof(one)) == ERROR_SUCCESS, "Couldn't finish installation registration.");
    } catch (...) { RegCloseKey(key); throw; }
    RegCloseKey(key);
    Link(env.menu, env.root / L"KiloNotch.exe");
    if (desktop) Link(env.desktop, env.root / L"KiloNotch.exe"); else RemoveLink(env.desktop, env.root / L"KiloNotch.exe");
    Require(RegCreateKeyExW(HKEY_CURRENT_USER, RunKey, 0, nullptr, 0, KEY_WRITE, nullptr, &key, nullptr) == ERROR_SUCCESS, "Couldn't update startup preferences.");
    if (startup) { try { StringValue(key, L"Notch", L"\"" + (env.root / L"KiloNotch.exe").wstring() + L"\""); } catch (...) { RegCloseKey(key); throw; } }
    else RegDeleteValueW(key, L"Notch");
    RegCloseKey(key);
}
static void Unregister(const Environment& env) {
    if (_wcsicmp(ReadString(ProductKey, L"InstallLocation").c_str(), env.root.c_str()) == 0) {
        const auto result = RegDeleteTreeW(HKEY_CURRENT_USER, ProductKey);
        Require(result == ERROR_SUCCESS || result == ERROR_FILE_NOT_FOUND, "Couldn't remove installation registration. Please retry.");
    }
    HKEY key{};
    const auto command = ReadString(RunKey, L"Notch");
    if (command == L"\"" + (env.root / L"KiloNotch.exe").wstring() + L"\"" && RegOpenKeyExW(HKEY_CURRENT_USER, RunKey, 0, KEY_SET_VALUE, &key) == ERROR_SUCCESS) { RegDeleteValueW(key, L"Notch"); RegCloseKey(key); }
    RemoveLink(env.menu, env.root / L"KiloNotch.exe"); RemoveLink(env.desktop, env.root / L"KiloNotch.exe");
}

void StopRunningApp() {
    HANDLE signal = OpenEventW(EVENT_MODIFY_STATE, FALSE, L"Local\\KiloNotch.Shutdown");
    if (signal) { SetEvent(signal); CloseHandle(signal); }
    // No forced termination: locked files leave the old installation intact and surface a retry.
}

static void CheckUnlocked(const Environment& env) {
    for (const auto& name : OwnedFiles) CheckPath(env.root / name);
    // Check every executable before removing either; a running process must not leave half an app.
    std::vector<HANDLE> handles;
    for (const auto& name : {L"KiloNotch.exe", L"KiloUninstall.exe"}) {
        const auto path = env.root / name; if (!fs::exists(path)) continue;
        HANDLE file = CreateFileW(path.c_str(), DELETE, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0, nullptr);
        if (file == INVALID_HANDLE_VALUE) { for (auto h : handles) CloseHandle(h); throw std::runtime_error("Close Kilo from the tray, then try again. Your installation is still here."); }
        handles.push_back(file);
    }
    for (auto h : handles) CloseHandle(h);
}
void Uninstall(const Environment& env, Progress progress) {
    Require(IsOwned(env.root), "This folder isn't a verified Kilo installation. Nothing was removed.");
    progress(15, L"Checking your installation");
    if (env.shell) { StopRunningApp(); Sleep(1600); }
    CheckUnlocked(env);
    progress(50, L"Removing application files");
    // Keep the marker until all removals succeed, so retry remains possible.
    if (env.shell) Unregister(env);
    RemoveKnown(env.root);
    progress(100, L"Your tasks, settings and photos are still yours");
}

#ifdef KILO_PAYLOAD
struct Extraction { fs::path root; std::set<std::wstring> found; };
static UINT CALLBACK ExtractCallback(PVOID context, UINT notification, UINT_PTR p1, UINT_PTR) {
    try {
    auto& extraction = *static_cast<Extraction*>(context);
    if (notification == SPFILENOTIFY_FILEINCABINET) {
        auto* file = reinterpret_cast<FILE_IN_CABINET_INFO_W*>(p1);
        const std::wstring name(file->NameInCabinet);
        if (!AllowedPayloadName(name) || !extraction.found.insert(name).second) return FILEOP_ABORT;
        const auto target = extraction.root / name;
        if (target.wstring().size() >= MAX_PATH) return FILEOP_ABORT;
        wcscpy_s(file->FullTargetName, target.c_str()); return FILEOP_DOIT;
    }
    if (notification == SPFILENOTIFY_FILEEXTRACTED) return reinterpret_cast<FILEPATHS_W*>(p1)->Win32Error;
    if (notification == SPFILENOTIFY_NEEDNEWCABINET) return ERROR_INVALID_DATA;
    return NO_ERROR;
    } catch (...) { return notification == SPFILENOTIFY_FILEINCABINET ? FILEOP_ABORT : ERROR_INVALID_DATA; }
}
struct ShellBackup {
    Environment env; fs::path storage; std::wstring startup; bool keyExisted = false;
    struct Value { std::wstring name; DWORD type; std::vector<BYTE> data; }; std::vector<Value> values;
    ShellBackup(const Environment& e, const fs::path& dir) : env(e), storage(dir), startup(ReadString(RunKey, L"Notch")) {
        if (!env.shell) return;
        fs::create_directory(storage);
        if (fs::exists(env.menu)) fs::copy_file(env.menu, storage / L"menu.lnk");
        if (fs::exists(env.desktop)) fs::copy_file(env.desktop, storage / L"desktop.lnk");
        HKEY key{}; if (RegOpenKeyExW(HKEY_CURRENT_USER, ProductKey, 0, KEY_READ, &key) != ERROR_SUCCESS) return;
        keyExisted = true;
        for (DWORD i = 0;; ++i) {
            wchar_t name[256]{}; DWORD count = 256, size = 32768, type = 0; std::vector<BYTE> data(size);
            const auto result = RegEnumValueW(key, i, name, &count, nullptr, &type, data.data(), &size);
            if (result == ERROR_NO_MORE_ITEMS) break;
            if (result != ERROR_SUCCESS) { RegCloseKey(key); throw std::runtime_error("Couldn't back up existing installation settings."); }
            data.resize(size); values.push_back({name, type, std::move(data)});
        }
        RegCloseKey(key);
    }
    void Restore() {
        if (!env.shell) return;
        RegDeleteTreeW(HKEY_CURRENT_USER, ProductKey); HKEY key{};
        if (keyExisted && RegCreateKeyExW(HKEY_CURRENT_USER, ProductKey, 0, nullptr, 0, KEY_WRITE, nullptr, &key, nullptr) == ERROR_SUCCESS) {
            for (const auto& value : values) RegSetValueExW(key, value.name.c_str(), 0, value.type, value.data.data(), static_cast<DWORD>(value.data.size()));
            RegCloseKey(key);
        }
        for (const auto& pair : { std::pair{env.menu, storage / L"menu.lnk"}, std::pair{env.desktop, storage / L"desktop.lnk"} }) {
            if (fs::exists(pair.second)) fs::copy_file(pair.second, pair.first, fs::copy_options::overwrite_existing);
            else if (fs::exists(pair.first)) fs::remove(pair.first);
        }
        if (RegCreateKeyExW(HKEY_CURRENT_USER, RunKey, 0, nullptr, 0, KEY_WRITE, nullptr, &key, nullptr) == ERROR_SUCCESS) {
            if (startup.empty()) RegDeleteValueW(key, L"Notch"); else StringValue(key, L"Notch", startup);
            RegCloseKey(key);
        }
    }
    ~ShellBackup() { std::error_code ec; fs::remove(storage / L"menu.lnk", ec); fs::remove(storage / L"desktop.lnk", ec); fs::remove(storage, ec); }
};
void Install(const Environment& env, const fs::path& cabinet, bool desktop, bool startup, Progress progress) {
    CheckPath(env.root); CheckPath(env.menu); CheckPath(env.desktop);
    const bool upgrade = fs::exists(env.root);
    if (upgrade) {
        Require(IsOwned(env.root), "The destination already contains files from another installation. Nothing was changed.");
        for (const auto& entry : fs::directory_iterator(env.root)) Require(std::find(OwnedFiles.begin(), OwnedFiles.end(), entry.path().filename().wstring()) != OwnedFiles.end(), "There are extra files in the installation folder. Move them somewhere safe before upgrading.");
        for (const auto& file : OwnedFiles) CheckPath(env.root / file);
    }
    fs::create_directories(env.root.parent_path());
    const auto token = Unique(); const auto stage = env.root.parent_path() / (L".kilo-stage-" + token); const auto backup = env.root.parent_path() / (L".kilo-backup-" + token);
    ShellBackup shell(env, env.root.parent_path() / (L".kilo-shell-" + token));
    fs::create_directory(stage); bool oldMoved = false, newMoved = false, shellChanged = false;
    try {
        progress(12, L"Unpacking a little company");
        Extraction extraction{stage, {}};
        Require(SetupIterateCabinetW(cabinet.c_str(), 0, ExtractCallback, &extraction) && extraction.found.size() == 2, "The package is incomplete. Download it again from GitHub.");
        progress(42, L"Verifying every installation file");
        Require(Sha256(stage / L"KiloNotch.exe") == AppSha256 && Sha256(stage / L"KiloUninstall.exe") == UninstallSha256, "Package verification failed. Nothing was installed.");
        { std::ofstream marker(stage / Marker); marker << Magic << '\n' << "1.3.0\n"; Require(marker.good(), "Couldn't write installation metadata."); }
        if (env.shell) { StopRunningApp(); Sleep(1600); }
        if (upgrade) CheckUnlocked(env);
        if (upgrade) { fs::rename(env.root, backup); oldMoved = true; }
        fs::rename(stage, env.root); newMoved = true;
        progress(75, L"Making Kilo feel at home");
        if (env.shell) { shellChanged = true; Register(env, desktop, startup); }
        progress(95, L"Finishing up");
    } catch (...) {
        // Restore files even if restoring a shell shortcut fails.
        if (shellChanged) { try { shell.Restore(); } catch (...) {} }
        if (newMoved) RemoveKnown(env.root);
        if (oldMoved) fs::rename(backup, env.root);
        if (fs::exists(stage)) RemoveKnown(stage);
        throw;
    }
    // The new installation is committed. Keep a backup if Windows still has an old file open.
    if (oldMoved) { try { RemoveKnown(backup); } catch (...) {} }
    progress(100, L"Kilo is ready when you are");
}
fs::path UnpackResource() {
    const auto resource = FindResourceW(nullptr, MAKEINTRESOURCEW(101), RT_RCDATA);
    Require(resource != nullptr, "This installer is missing its package.");
    const auto size = SizeofResource(nullptr, resource); const auto data = LoadResource(nullptr, resource);
    Require(data != nullptr && size > 0, "Couldn't read the installation package.");
    const auto folder = fs::temp_directory_path() / (L"KiloNotch-setup-" + Unique()); CheckPath(folder); fs::create_directory(folder);
    const auto path = folder / L"payload.cab";
    std::ofstream file(path, std::ios::binary); file.write(static_cast<const char*>(LockResource(data)), size);
    Require(file.good(), "Couldn't unpack the installer. Check available disk space."); return path;
}
#endif
}
