#include "engine.h"
#include <objbase.h>
#include <fstream>
#include <iostream>
#include <stdexcept>
using namespace kilo;
static int checks = 0;
static void Check(bool ok, const char* name) { if (!ok) throw std::runtime_error(name); ++checks; std::cout << "PASS " << name << '\n'; }
template<class F> void Reject(F action, const char* name) { bool rejected = false; try { action(); } catch (const std::exception&) { rejected = true; } Check(rejected, name); }
int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 2;
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    GUID id{}; CoCreateGuid(&id); wchar_t token[40]{}; StringFromGUID2(id, token, 40);
    const auto sandbox = fs::temp_directory_path() / (std::wstring(L"KiloInstallerTest-") + token);
    try {
        CheckPath(sandbox); fs::create_directory(sandbox);
        const Environment env{sandbox / L"app", sandbox / L"menu.lnk", sandbox / L"desktop.lnk", false};
        const fs::path cab = fs::absolute(argv[1]);
        const auto quiet = [](int, const std::wstring&) {};
        Check(!AllowedPayloadName(L"../KiloNotch.exe") && !AllowedPayloadName(L"else.exe") && AllowedPayloadName(L"KiloNotch.exe"), "payload allowlist rejects traversal and extra files");
        Reject([] { CheckPath(L"relative/path"); }, "relative destination rejected");
        fs::create_directory(env.root); std::ofstream(env.root / L"personal.txt") << "keep";
        Reject([&] { Install(env, cab, false, false, quiet); }, "foreign folder never overwritten");
        Reject([&] { Uninstall(env, quiet); }, "foreign folder never uninstalled");
        fs::remove(env.root / L"personal.txt"); fs::remove(env.root);
        Install(env, cab, false, false, quiet);
        Check(IsOwned(env.root) && fs::exists(env.root / L"KiloUninstall.exe"), "fresh install verifies and writes both executables");
        const auto expected = Sha256(env.root / L"KiloNotch.exe");
        std::ofstream(env.root / L"KiloNotch.exe", std::ios::binary | std::ios::trunc) << "old version fixture";
        const auto old = Sha256(env.root / L"KiloNotch.exe");
        Reject([&] { Install(env, cab, false, false, [](int p, const std::wstring&) { if (p == 75) throw std::runtime_error("injected upgrade failure"); }); }, "upgrade failure injected after directory swap");
        Check(IsOwned(env.root) && Sha256(env.root / L"KiloNotch.exe") == old, "upgrade rollback restores previous binary");
        Install(env, cab, false, false, quiet);
        Check(Sha256(env.root / L"KiloNotch.exe") == expected, "successful upgrade replaces old binary");
        HANDLE locked = CreateFileW((env.root / L"KiloNotch.exe").c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr);
        Check(locked != INVALID_HANDLE_VALUE, "open lock fixture");
        Reject([&] { Install(env, cab, false, false, quiet); }, "locked upgrade leaves existing installation intact");
        Reject([&] { Uninstall(env, quiet); }, "locked uninstall aborts before removing any file");
        CloseHandle(locked);
        Check(fs::exists(env.root / L"KiloUninstall.exe") && IsOwned(env.root), "locked install still complete");
        const auto corrupt = sandbox / L"corrupt.cab"; std::ofstream(corrupt) << "invalid cabinet";
        Reject([&] { Install(env, corrupt, false, false, quiet); }, "corrupt package refused");
        Check(Sha256(env.root / L"KiloNotch.exe") == expected, "corrupt update preserves current version");
        const auto outside = sandbox / L"user-data"; fs::create_directory(outside); std::ofstream(outside / L"data.json") << "keep";
        const auto linked = sandbox / L"linked";
        Check(CreateSymbolicLinkW(linked.c_str(), outside.c_str(), SYMBOLIC_LINK_FLAG_DIRECTORY | SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE) != 0, "create linked-directory boundary fixture");
        Reject([&] { CheckPath(linked / L"app"); }, "junction or symbolic-link ancestry rejected");
        fs::remove(linked);
        std::ofstream(env.root / L"notes.txt") << "keep";
        Reject([&] { Install(env, cab, false, false, quiet); }, "upgrade preserves unexpected user files");
        Uninstall(env, quiet);
        Check(fs::exists(env.root / L"notes.txt") && fs::exists(outside / L"data.json"), "uninstall preserves personal files inside and outside destination");
        Check(!fs::exists(env.root / L"KiloNotch.exe") && !fs::exists(env.root / L".kilo-install"), "uninstall removes only owned application files");
        const Environment shellEnv{sandbox / L"registered-app", sandbox / L"shortcuts" / L"menu.lnk", sandbox / L"shortcuts" / L"desktop.lnk", true};
        Install(shellEnv, cab, true, true, quiet);
        Check(fs::exists(shellEnv.menu) && fs::exists(shellEnv.desktop), "per-user shortcuts created");
        const auto menuHash = Sha256(shellEnv.menu);
        Reject([&] { Install(shellEnv, cab, false, false, [](int p, const std::wstring&) { if (p == 95) throw std::runtime_error("injected shell registration failure"); }); }, "failure after changing registry and shortcuts");
        Check(IsOwned(shellEnv.root) && fs::exists(shellEnv.desktop) && Sha256(shellEnv.menu) == menuHash, "shell and binaries restored together");
        wchar_t startup[32768]{}; DWORD startupSize = sizeof(startup);
        Check(RegGetValueW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Run", L"Notch", RRF_RT_REG_SZ, nullptr, startup, &startupSize) == ERROR_SUCCESS && std::wstring(startup).find(shellEnv.root.wstring()) != std::wstring::npos, "startup restored after rollback");
        Uninstall(shellEnv, quiet);
        Check(!fs::exists(shellEnv.menu), "uninstall removes its Start menu shortcut");
        Check(!fs::exists(shellEnv.desktop), "uninstall removes its desktop shortcut");
        Check(!fs::exists(shellEnv.root), "uninstall removes its registered application folder");
        for (const auto& entry : fs::directory_iterator(sandbox)) Check(entry.path().filename().wstring().find(L".kilo-") != 0, "transaction folders cleaned");
        // Only a GUID-named temporary fixture created by this test is recursively removed.
        Check(sandbox.parent_path() == fs::temp_directory_path(), "test cleanup boundary");
        fs::remove_all(sandbox); CoUninitialize();
        std::cout << checks << " installer checks passed\n"; return 0;
    } catch (const std::exception& ex) { std::cerr << ex.what() << '\n'; CoUninitialize(); return 1; }
}
