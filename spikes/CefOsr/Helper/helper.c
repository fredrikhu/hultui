// Spike L0, question 1: the subprocess executable that works. Chromium's zygote must be
// single-threaded to enter its sandbox, and no .NET executable is (NativeAOT starts with a
// finalizer thread, JIT with six more), so this is the one piece of native code: dlopen
// libcef, name the API version (the same one the browser process named), and hand over.
// Build: clang -O2 -o cefosr-helper-c helper.c
#include <dlfcn.h>
#include <stdio.h>
#include <stdlib.h>
typedef struct { int argc; char** argv; } main_args;
int main(int argc, char** argv) {
  const char* dir = getenv("HULTUI_CEF_DIR"); char path[4096];
  snprintf(path, sizeof path, "%s/libcef.so", dir ? dir : "/usr/lib64/cef");
  void* lib = dlopen(path, RTLD_NOW);
  if (!lib) { fprintf(stderr, "chelper: %s\n", dlerror()); return 1; }
  const char* (*hash)(int, int) = dlsym(lib, "cef_api_hash");
  int (*exec)(const main_args*, void*, void*) = dlsym(lib, "cef_execute_process");
  hash(14600, 0);
  main_args a = { argc, argv };
  return exec(&a, NULL, NULL);
}
