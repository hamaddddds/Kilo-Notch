#pragma once
#include <windows.h>
#include <filesystem>
#include <functional>
#include <string>

namespace kilo {
namespace fs = std::filesystem;
struct Environment {
    fs::path root, menu, desktop;
    bool shell = true;
};
using Progress = std::function<void(int, const std::wstring&)>;
Environment CurrentEnvironment();
fs::path OwnExecutable();
bool IsOwned(const fs::path& root);
void CheckPath(const fs::path& path);
bool AllowedPayloadName(const std::wstring& name);
std::wstring Sha256(const fs::path& path);
void StopRunningApp();
void Uninstall(const Environment& env, Progress progress);
#ifdef KILO_PAYLOAD
void Install(const Environment& env, const fs::path& cabinet, bool desktop, bool startup, Progress progress);
fs::path UnpackResource();
#endif
}
