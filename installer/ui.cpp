#include "engine.h"
#include "Version.h"
#include <windowsx.h>
#include <d2d1.h>
#include <dwrite.h>
#include <dwmapi.h>
#include <shellapi.h>
#include <wrl/client.h>
#include <thread>
#include <memory>
#include <cmath>
#include <algorithm>
#include <fstream>
#include <vector>

using Microsoft::WRL::ComPtr;
using namespace kilo;
namespace {
constexpr float W = 860, H = 560;
constexpr UINT UpdateMessage = WM_APP + 1, FinishMessage = WM_APP + 2;
#ifdef KILO_UNINSTALL
constexpr bool Removing = true;
#else
constexpr bool Removing = false;
#endif
struct Update { int percent; std::wstring text; };
HWND window{};
ComPtr<ID2D1Factory> factory;
ComPtr<IDWriteFactory> writeFactory;
ComPtr<ID2D1HwndRenderTarget> canvas;
ComPtr<ID2D1SolidColorBrush> brush;
std::thread worker;
Environment environment;
bool busy = false, complete = false, failed = false, desktop = true, startup = false;
bool upgrading = false;
bool smoke = false;
int smokeExit = 0, paints = 0;
int progress = 0, focus = 0, hover = -1;
float displayedProgress = 0, dpi = 96;
std::wstring status = L"", detail = L"";
const D2D1_RECT_F Primary = D2D1::RectF(390, 426, 800, 480);
const D2D1_RECT_F Desktop = D2D1::RectF(390, 302, 800, 336);
const D2D1_RECT_F Startup = D2D1::RectF(390, 345, 800, 379);
const D2D1_RECT_F Close = D2D1::RectF(802, 12, 844, 48);

D2D1_COLOR_F Color(unsigned rgb, float alpha = 1) { return D2D1::ColorF(rgb, alpha); }
void Fill(D2D1_RECT_F box, unsigned color, float radius = 0, float alpha = 1) {
    brush->SetColor(Color(color, alpha));
    if (radius > 0) canvas->FillRoundedRectangle(D2D1::RoundedRect(box, radius, radius), brush.Get()); else canvas->FillRectangle(box, brush.Get());
}
void Line(float x, float y, float x2, float y2, unsigned color, float width = 1) {
    brush->SetColor(Color(color)); canvas->DrawLine(D2D1::Point2F(x, y), D2D1::Point2F(x2, y2), brush.Get(), width);
}
void Text(const std::wstring& text, D2D1_RECT_F area, float size, unsigned color, bool bold = false, DWRITE_TEXT_ALIGNMENT align = DWRITE_TEXT_ALIGNMENT_LEADING) {
    ComPtr<IDWriteTextFormat> format;
    if (FAILED(writeFactory->CreateTextFormat(L"Segoe UI", nullptr, bold ? DWRITE_FONT_WEIGHT_SEMI_BOLD : DWRITE_FONT_WEIGHT_NORMAL, DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, size, L"en-us", &format))) return;
    format->SetTextAlignment(align); format->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_NEAR); format->SetWordWrapping(DWRITE_WORD_WRAPPING_WRAP);
    brush->SetColor(Color(color)); canvas->DrawTextW(text.c_str(), static_cast<UINT32>(text.size()), format.Get(), area, brush.Get(), D2D1_DRAW_TEXT_OPTIONS_CLIP);
}
bool Inside(D2D1_RECT_F box, float x, float y) { return x >= box.left && x <= box.right && y >= box.top && y <= box.bottom; }
void Option(D2D1_RECT_F box, const wchar_t* label, bool checked, int index) {
    if (hover == index) Fill(box, 0xe9e9e2, 8);
    const auto square = D2D1::RectF(box.left + 2, box.top + 7, box.left + 22, box.top + 27);
    Fill(square, checked ? 0x2546ef : 0xe0e1da, 5);
    if (checked) { Line(square.left + 5, square.top + 10, square.left + 9, square.top + 14, 0xffffff, 2); Line(square.left + 9, square.top + 14, square.left + 15, square.top + 6, 0xffffff, 2); }
    Text(label, D2D1::RectF(box.left + 36, box.top + 6, box.right, box.bottom), 14, 0x263127);
    if (focus == index) { brush->SetColor(Color(0x2546ef)); canvas->DrawRoundedRectangle(D2D1::RoundedRect(box, 7, 7), brush.Get(), 1.5f); }
}
void Draw() {
    if (!canvas) {
        RECT rect{}; GetClientRect(window, &rect);
        const auto hr = factory->CreateHwndRenderTarget(D2D1::RenderTargetProperties(), D2D1::HwndRenderTargetProperties(window, D2D1::SizeU(static_cast<UINT>(rect.right), static_cast<UINT>(rect.bottom))), &canvas);
        if (FAILED(hr)) return;
        canvas->SetDpi(dpi, dpi); canvas->CreateSolidColorBrush(Color(0x000000), &brush);
        if (!brush) { canvas.Reset(); return; }
    }
    canvas->BeginDraw(); canvas->Clear(Color(0xf4f4ee));
    Fill(D2D1::RectF(0, 0, 338, H), 0x101b2a);
    Text(L"kilo", D2D1::RectF(36, 28, 180, 68), 26, 0xf5f6f0, true);
    Text(Removing ? L"UNINSTALL" : L"WINDOWS SETUP", D2D1::RectF(390, 33, 700, 60), 11, 0x757e72, true);
    // A small desktop vignette: everything here is drawn, including every control.
    Fill(D2D1::RectF(32, 113, 306, 300), 0x25374a, 14);
    Fill(D2D1::RectF(32, 113, 306, 139), 0x1b2b3d, 14);
    for (int i = 0; i < 3; ++i) Fill(D2D1::RectF(46.0f + static_cast<float>(i) * 11, 123, 50.0f + static_cast<float>(i) * 11, 127), 0x647184, 2);
    Fill(D2D1::RectF(102, 139, 236, 181), 0x05090d, 18);
    const float time = static_cast<float>(GetTickCount64() % 4500) / 4500;
    const float eyeHeight = time > .95f && time < .98f ? 3.0f : complete ? 9.0f : 16.0f;
    Fill(D2D1::RectF(151, 158 - eyeHeight / 2, 160, 158 + eyeHeight / 2), 0xcaf6ff, 4);
    Fill(D2D1::RectF(178, 158 - eyeHeight / 2, 187, 158 + eyeHeight / 2), 0xcaf6ff, 4);
    Fill(D2D1::RectF(57, 210, 177, 216), 0x8b9ba8, 3);
    Fill(D2D1::RectF(57, 230, 256, 235), 0x485c70, 2);
    Fill(D2D1::RectF(57, 246, 230, 251), 0x485c70, 2);
    Fill(D2D1::RectF(57, 270, 111, 280), 0x7188ac, 5);
    Text(Removing ? L"See you\naround." : L"A little space.\nA little company.", D2D1::RectF(36, 334, 312, 432), 30, 0xf4f5ef, true);
    Text(Removing ? L"Your things stay with you." : L"Music, small tasks, and a pair of eyes\nthat make the desktop feel yours.", D2D1::RectF(36, 452, 300, 513), 13, 0xa4b4c2);
    Text(std::wstring(L"KILO NOTCH  /  ") + KiloVersion, D2D1::RectF(36, 526, 300, 550), 9, 0x8296a9);

    const auto heading = complete ? (Removing ? L"All packed up." : L"Make yourself\nat home.") : busy ? (Removing ? L"Saying goodbye." : L"Moving in.") : failed ? L"One small pause." : Removing ? L"A clean goodbye." : upgrading ? L"A little\nsomething new." : L"Meet your\nnew desk mate.";
    Text(heading, D2D1::RectF(390, 94, 802, 205), 39, 0x17261e, true);
    std::wstring description;
    if (failed) description = detail;
    else if (complete) description = Removing ? L"The app is removed. Your saved tasks, settings and photo strips haven't been touched." : L"Kilo is installed. Hover at the top of your screen to open it. Right-click for settings.";
    else if (busy) description = status;
    else description = Removing ? L"Remove Kilo from this Windows account. Your tasks, settings and photos will stay exactly where they are." : L"A native Windows app, installed just for you. Everything it needs is included. No account, no extra runtime download.";
    Text(description, D2D1::RectF(390, 219, 800, 294), 14, failed ? 0x9f412b : 0x687367);
    if (!busy && !complete && !failed && !Removing) { Option(Desktop, L"Put a shortcut on my desktop", desktop, 1); Option(Startup, L"Keep me company when Windows starts", startup, 2); }
    else if (busy) {
        displayedProgress += (static_cast<float>(progress) - displayedProgress) * .12f;
        Fill(D2D1::RectF(390, 329, 800, 335), 0xe0e4d9, 3);
        Fill(D2D1::RectF(390, 329, 390 + 410 * displayedProgress / 100, 335), 0x2546ef, 3);
        Text(std::to_wstring(progress) + L"%", D2D1::RectF(390, 354, 800, 382), 13, 0x687367);
    } else if (complete) {
        Fill(D2D1::RectF(390, 318, 426, 354), 0xe0e9d6, 18);
        Line(401, 336, 407, 342, 0x3e6c40, 2); Line(407, 342, 416, 330, 0x3e6c40, 2);
        Text(Removing ? L"Personal files kept safe" : L"Ready for your first hello", D2D1::RectF(439, 327, 800, 360), 14, 0x3e6c40);
    }
    Fill(Primary, busy ? 0xdce0d6 : hover == 0 ? 0x1534d5 : 0x2546ef, 12);
    std::wstring action = busy ? L"Just a moment..." : failed ? L"Try again" : complete ? (Removing ? L"Close" : L"Open Kilo") : Removing ? L"Remove Kilo" : upgrading ? L"Update Kilo" : L"Install Kilo";
    Text(action, D2D1::RectF(390, 442, 800, 473), 15, busy ? 0x75816f : 0xffffff, true, DWRITE_TEXT_ALIGNMENT_CENTER);
    if (focus == 0 && !busy) { brush->SetColor(Color(0x2546ef)); canvas->DrawRoundedRectangle(D2D1::RoundedRect(D2D1::RectF(386, 422, 804, 484), 15, 15), brush.Get(), 1.5f); }
    Text(Removing ? L"You can install Kilo again whenever you like." : L"Local AppData  /  Programs  /  Kilo Notch", D2D1::RectF(390, 503, 800, 531), 11, 0x818a7c);
    if (hover == 3 || focus == 3) Fill(Close, 0xe7e8df, 9);
    Line(818, 25, 828, 35, 0x75816f, 1.5f); Line(828, 25, 818, 35, 0x75816f, 1.5f);
    const auto result = canvas->EndDraw(); if (result == D2DERR_RECREATE_TARGET) { brush.Reset(); canvas.Reset(); }
    if (SUCCEEDED(result)) ++paints;
}
bool SaveSmokeFrame() {
    RECT rect{}; GetClientRect(window, &rect);
    HDC source = GetDC(window); HDC target = CreateCompatibleDC(source);
    BITMAPINFO info{}; info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER); info.bmiHeader.biWidth = rect.right;
    info.bmiHeader.biHeight = -rect.bottom; info.bmiHeader.biPlanes = 1; info.bmiHeader.biBitCount = 32; info.bmiHeader.biCompression = BI_RGB;
    void* pixels = nullptr; HBITMAP bitmap = CreateDIBSection(source, &info, DIB_RGB_COLORS, &pixels, nullptr, 0);
    if (!source || !target || !bitmap || !pixels) { if (bitmap) DeleteObject(bitmap); if (target) DeleteDC(target); if (source) ReleaseDC(window, source); return false; }
    HGDIOBJ old = SelectObject(target, bitmap);
    const bool copied = BitBlt(target, 0, 0, rect.right, rect.bottom, source, 0, 0, SRCCOPY) != 0;
    const DWORD bytes = static_cast<DWORD>(rect.right * rect.bottom * 4);
    const auto data = static_cast<const unsigned char*>(pixels);
    bool varied = false; for (DWORD i = 4; i + 4 < bytes; i += 4) if (data[i] != data[0] || data[i+1] != data[1] || data[i+2] != data[2]) { varied = true; break; }
    BITMAPFILEHEADER header{}; header.bfType = 0x4d42; header.bfOffBits = sizeof(header) + sizeof(BITMAPINFOHEADER); header.bfSize = header.bfOffBits + bytes;
    fs::create_directories(L"artifacts/qa");
    std::ofstream file(Removing ? L"artifacts/qa/uninstaller.bmp" : L"artifacts/qa/installer.bmp", std::ios::binary);
    file.write(reinterpret_cast<const char*>(&header), sizeof(header)); file.write(reinterpret_cast<const char*>(&info.bmiHeader), sizeof(info.bmiHeader)); file.write(static_cast<const char*>(pixels), bytes);
    const bool saved = file.good(); SelectObject(target, old); DeleteObject(bitmap); DeleteDC(target); ReleaseDC(window, source);
    return copied && varied && saved;
}
std::wstring Wide(const char* text) {
    const int count = MultiByteToWideChar(CP_UTF8, 0, text, -1, nullptr, 0); if (count <= 0) return L"Something went wrong. Please try again.";
    std::wstring result(static_cast<size_t>(count), L'\0'); MultiByteToWideChar(CP_UTF8, 0, text, -1, result.data(), count); result.pop_back(); return result;
}
void Start() {
    if (busy) return;
    if (complete) {
        if (!Removing) ShellExecuteW(window, L"open", (environment.root / L"KiloNotch.exe").c_str(), nullptr, environment.root.c_str(), SW_SHOWNORMAL);
        DestroyWindow(window); return;
    }
    if (worker.joinable()) worker.join();
    busy = true; failed = false; progress = 0; displayedProgress = 0; status = L"Getting things ready";
    worker = std::thread([] {
        const auto co = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        fs::path cabinet;
        std::unique_ptr<std::wstring> error;
        try {
            const auto report = [](int p, const std::wstring& text) { auto update = std::make_unique<Update>(Update{p, text}); if (PostMessageW(window, UpdateMessage, 0, reinterpret_cast<LPARAM>(update.get()))) update.release(); };
#ifdef KILO_PAYLOAD
            cabinet = UnpackResource(); Install(environment, cabinet, desktop, startup, report);
#else
            Uninstall(environment, report);
#endif
        } catch (const std::exception& ex) { error = std::make_unique<std::wstring>(Wide(ex.what())); }
        if (!cabinet.empty()) { std::error_code ec; fs::remove(cabinet, ec); fs::remove(cabinet.parent_path(), ec); }
        if (SUCCEEDED(co)) CoUninitialize();
        if (PostMessageW(window, FinishMessage, error ? 1 : 0, reinterpret_cast<LPARAM>(error.get()))) error.release();
    });
}
void Activate(int which) {
    if (which == 3) { if (!busy) DestroyWindow(window); return; }
    if (busy) return;
    if (which == 0) Start();
    else if (!Removing && !complete && !failed) { if (which == 1) desktop = !desktop; if (which == 2) startup = !startup; }
    InvalidateRect(window, nullptr, FALSE);
}
LRESULT CALLBACK Procedure(HWND hwnd, UINT message, WPARAM wparam, LPARAM lparam) {
    switch (message) {
    case WM_PAINT: { PAINTSTRUCT paint{}; BeginPaint(hwnd, &paint); Draw(); EndPaint(hwnd, &paint); return 0; }
    case WM_ERASEBKGND: return 1;
    case WM_TIMER:
        if (smoke && paints >= 8) {
            // Exercise custom keyboard navigation without starting an installation.
            SendMessageW(hwnd, WM_KEYDOWN, VK_TAB, 0);
            const bool navigated = focus == (Removing ? 3 : 1);
            smokeExit = navigated && GetWindow(hwnd, GW_CHILD) == nullptr && SaveSmokeFrame() ? 0 : 1;
            DestroyWindow(hwnd); return 0;
        }
        InvalidateRect(hwnd, nullptr, FALSE); return 0;
    case WM_NCHITTEST: { POINT point{GET_X_LPARAM(lparam), GET_Y_LPARAM(lparam)}; ScreenToClient(hwnd, &point); const float x = point.x * 96.0f / dpi, y = point.y * 96.0f / dpi; if (y < 60 && !Inside(Close, x, y)) return HTCAPTION; return HTCLIENT; }
    case WM_MOUSEMOVE: { const float x = GET_X_LPARAM(lparam) * 96.0f / dpi, y = GET_Y_LPARAM(lparam) * 96.0f / dpi; hover = Inside(Close,x,y) ? 3 : Inside(Primary,x,y) ? 0 : Inside(Desktop,x,y) ? 1 : Inside(Startup,x,y) ? 2 : -1; SetCursor(LoadCursorW(nullptr, hover >= 0 ? IDC_HAND : IDC_ARROW)); return 0; }
    case WM_LBUTTONUP: { const float x = GET_X_LPARAM(lparam) * 96.0f / dpi, y = GET_Y_LPARAM(lparam) * 96.0f / dpi; const int hit = Inside(Close,x,y) ? 3 : Inside(Primary,x,y) ? 0 : Inside(Desktop,x,y) ? 1 : Inside(Startup,x,y) ? 2 : -1; if (hit >= 0) { focus = hit; Activate(hit); } return 0; }
    case WM_KEYDOWN:
        if (wparam == VK_ESCAPE) Activate(3);
        else if (wparam == VK_TAB) { if (Removing || complete || failed) focus = focus == 0 ? 3 : 0; else focus = (focus + (GetKeyState(VK_SHIFT) < 0 ? 3 : 1)) % 4; InvalidateRect(hwnd, nullptr, FALSE); }
        else if (wparam == VK_RETURN || wparam == VK_SPACE) Activate(focus);
        return 0;
    case WM_DPICHANGED: { dpi = static_cast<float>(HIWORD(wparam)); const auto rect = reinterpret_cast<RECT*>(lparam); SetWindowPos(hwnd, nullptr, rect->left, rect->top, rect->right - rect->left, rect->bottom - rect->top, SWP_NOZORDER | SWP_NOACTIVATE); if (canvas) { canvas->SetDpi(dpi,dpi); canvas->Resize(D2D1::SizeU(static_cast<UINT>(rect->right - rect->left),static_cast<UINT>(rect->bottom - rect->top))); } return 0; }
    case UpdateMessage: { std::unique_ptr<Update> update(reinterpret_cast<Update*>(lparam)); progress = update->percent; status = update->text; return 0; }
    case FinishMessage: { std::unique_ptr<std::wstring> error(reinterpret_cast<std::wstring*>(lparam)); busy = false; failed = wparam != 0; complete = !failed; if (error) detail = *error; focus = 0; SetWindowTextW(hwnd, failed ? L"Kilo — installation needs attention" : Removing ? L"Kilo — uninstalled" : L"Kilo — ready"); return 0; }
    case WM_CLOSE: if (!busy) DestroyWindow(hwnd); return 0;
    case WM_DESTROY: KillTimer(hwnd, 1); if (worker.joinable()) worker.join(); PostQuitMessage(0); return 0;
    default: return DefWindowProcW(hwnd, message, wparam, lparam);
    }
}
bool RelaunchUninstaller() {
    if (!Removing || OwnExecutable().parent_path() != environment.root) return false;
    GUID id{}; CoCreateGuid(&id); wchar_t name[40]{}; StringFromGUID2(id,name,40);
    const auto directory = fs::temp_directory_path() / (std::wstring(L"KiloNotch-uninstall-") + name); CheckPath(directory); fs::create_directory(directory);
    const auto helper = directory / L"KiloUninstall.exe"; fs::copy_file(OwnExecutable(), helper);
    const auto result = reinterpret_cast<INT_PTR>(ShellExecuteW(nullptr, L"open", helper.c_str(), nullptr, directory.c_str(), SW_SHOWNORMAL));
    if (result <= 32) throw std::runtime_error("Couldn't start the uninstaller. Please try again.");
    return true;
}
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR command, int show) {
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    smoke = std::wstring(command) == L"--smoke";
    wchar_t ci[8]{}; if (smoke && (!GetEnvironmentVariableW(L"GITHUB_ACTIONS", ci, 8) || std::wstring(ci) != L"true")) { CoUninitialize(); return 2; }
    try { environment = CurrentEnvironment(); if (RelaunchUninstaller()) { CoUninitialize(); return 0; } upgrading = IsOwned(environment.root); if (upgrading) { startup = StartsWithWindows(environment); desktop = fs::exists(environment.desktop); } }
    catch (const std::exception& ex) { failed = true; detail = Wide(ex.what()); }
    // Serialize setup and uninstall; the original uninstaller exits before the helper owns this.
    HANDLE singleton = CreateMutexW(nullptr, FALSE, L"Local\\KiloNotch.Setup");
    if (!singleton || WaitForSingleObject(singleton, 0) == WAIT_TIMEOUT) { if (singleton) CloseHandle(singleton); CoUninitialize(); return 0; }
    if (FAILED(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, factory.GetAddressOf())) || FAILED(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory), reinterpret_cast<IUnknown**>(writeFactory.GetAddressOf())))) return 1;
    WNDCLASSW cls{}; cls.lpfnWndProc = Procedure; cls.hInstance = instance; cls.lpszClassName = L"KiloCustomSetup"; cls.hCursor = LoadCursorW(nullptr, IDC_ARROW); cls.hIcon = LoadIconW(instance, MAKEINTRESOURCEW(1)); RegisterClassW(&cls);
    dpi = static_cast<float>(GetDpiForSystem());
    const int width = static_cast<int>(W * dpi / 96), height = static_cast<int>(H * dpi / 96);
    window = CreateWindowExW(0, cls.lpszClassName, Removing ? L"Kilo Notch — Uninstall" : L"Kilo Notch — Setup", WS_POPUP,
        (GetSystemMetrics(SM_CXSCREEN) - width) / 2, (GetSystemMetrics(SM_CYSCREEN) - height) / 2, width, height, nullptr, nullptr, instance, nullptr);
    if (!window) return 1;
    const DWORD rounded = 2; DwmSetWindowAttribute(window, 33, &rounded, sizeof(rounded));
    ShowWindow(window, show); SetTimer(window, 1, 40, nullptr);
    MSG message{}; while (GetMessageW(&message, nullptr, 0, 0) > 0) { TranslateMessage(&message); DispatchMessageW(&message); }
    brush.Reset(); canvas.Reset(); factory.Reset(); writeFactory.Reset(); ReleaseMutex(singleton); CloseHandle(singleton); CoUninitialize(); return smokeExit;
}
