// The subprocess executable of HultUI.WebView.Cef: what Chromium starts for its zygote,
// renderer, GPU and utility processes (browser_subprocess_path).
//
// It is C because it has to be: Chromium's zygote must be single-threaded to enter its
// sandbox, and cannot fork a .NET process even unsandboxed, and every .NET executable has
// threads before Main (spike L0, question 1). It is the engine's only native code.
//
// It knows neither the CEF release nor the engine's API version. The browser process (the
// application, through CefEngine) sets both in the environment before cef_initialize, and
// every child inherits it:
//   HULTUI_CEF_DIR          the directory holding libcef.so
//   HULTUI_CEF_API_VERSION  the CEF API version the engine was generated for (CEF_API_VERSION)
// The handshake must name the same version in every process of the browser.
//
// Build: cc -O2 -o hultui-cef-helper hultui-cef-helper.c -ldl   (build.sh does this)

#include <dlfcn.h>
#include <stdio.h>
#include <stdlib.h>

typedef struct {
  int argc;
  char** argv;
} cef_main_args_t;

int main(int argc, char** argv) {
  const char* dir = getenv("HULTUI_CEF_DIR");
  const char* version = getenv("HULTUI_CEF_API_VERSION");
  if (!dir || !version) {
    fprintf(stderr, "hultui-cef-helper: started outside a HultUI.WebView.Cef browser "
                    "(HULTUI_CEF_DIR and HULTUI_CEF_API_VERSION are not set)\n");
    return 1;
  }

  char path[4096];
  snprintf(path, sizeof path, "%s/libcef.so", dir);
  void* lib = dlopen(path, RTLD_NOW);
  if (!lib) {
    fprintf(stderr, "hultui-cef-helper: %s\n", dlerror());
    return 1;
  }

  const char* (*api_hash)(int, int) = (const char* (*)(int, int))dlsym(lib, "cef_api_hash");
  int (*execute_process)(const cef_main_args_t*, void*, void*) =
      (int (*)(const cef_main_args_t*, void*, void*))dlsym(lib, "cef_execute_process");
  if (!api_hash || !execute_process) {
    fprintf(stderr, "hultui-cef-helper: %s is not a CEF library\n", path);
    return 1;
  }
  if (!api_hash(atoi(version), 0)) {
    fprintf(stderr, "hultui-cef-helper: %s does not offer CEF API version %s\n", path, version);
    return 1;
  }

  cef_main_args_t args = {argc, argv};
  return execute_process(&args, NULL, NULL);
}
