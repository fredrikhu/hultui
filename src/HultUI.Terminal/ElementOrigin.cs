// Global usings are per assembly, so this repeats HultUI.Core's alias for the
// element factories here: with HULTUI_ELEMENT_ORIGIN the caller-info attributes
// capture the call site for the DevTools inspector; without it the inert stand-in
// keeps the signature identical. See ElementOrigin.cs in HultUI.Core.
#if HULTUI_ELEMENT_ORIGIN
global using OriginFile = System.Runtime.CompilerServices.CallerFilePathAttribute;
global using OriginLine = System.Runtime.CompilerServices.CallerLineNumberAttribute;
#else
global using OriginFile = HultUI.Core.Elements.InertCallerInfoAttribute;
global using OriginLine = HultUI.Core.Elements.InertCallerInfoAttribute;
#endif
