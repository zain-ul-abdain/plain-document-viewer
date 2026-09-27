# Third-party notices

## Markdig 1.4.0

Source: https://github.com/xoofx/markdig
Pinned source commit: 56e9c238584a44a169f174c881855c049768634c

Copyright (c) 2016-2026, Alexandre Mutel
All rights reserved.

Redistribution and use in source and binary forms, with or without modification
, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this 
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice, 
   this list of conditions and the following disclaimer in the documentation 
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND 
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED 
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE 
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL 
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR 
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER 
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE 
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

## .NET and WPF

This development build uses a separately installed .NET 10 runtime. The SDK under .tools is a development dependency, not part of a distributable installer. Preserve the runtime notices when preparing a self-contained distribution.

## Microsoft Edge WebView2 SDK 1.0.4191.47

Source: https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4191.47 (assemblies Microsoft.Web.WebView2.Core, .Wpf, .WinForms and WebView2Loader.dll). The WebView2 Runtime itself is part of Windows 11 and is not bundled.

Copyright (C) Microsoft Corporation. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are
met:

   * Redistributions of source code must retain the above copyright
notice, this list of conditions and the following disclaimer.
   * Redistributions in binary form must reproduce the above
copyright notice, this list of conditions and the following disclaimer
in the documentation and/or other materials provided with the
distribution.
   * The name of Microsoft Corporation, or the names of its contributors
may not be used to endorse or promote products derived from this
software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

## PDF.js 6.3.289 (pdfjs-dist)

Source: https://github.com/mozilla/pdf.js, npm package pdfjs-dist 6.3.289 (integrity sha512-ZHjSVpDa3D6izMq8/04lvkhkATUmL9px6ChPaXc1k6nU2Mrhlg1/7F0bdUqCwUjw3NsPTfPZsMDUU6ZIcRaeQw==). Unmodified files are shipped in `Assets/pdf/pdfjs`.

Copyright Mozilla Foundation. Licensed under the Apache License, Version 2.0; the full licence text ships as `Assets/pdf/pdfjs/LICENSE`. You may not use these files except in compliance with the License. Distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND.

Components bundled inside PDF.js, each with its licence file next to it:

| Component | Files | Licence |
|---|---|---|
| Foxit standard fonts (from PDFium) | `standard_fonts/Foxit*.pfb` | BSD-3-Clause, `standard_fonts/LICENSE_FOXIT` |
| Liberation Sans 1.x | `standard_fonts/LiberationSans-*.ttf` | GPL v2 with font exception (Red Hat), `standard_fonts/LICENSE_LIBERATION`. Separate font files; they do not make the application a derivative work. Planned: replace with Liberation 2.x (OFL-1.1) before release |
| OpenJPEG decoder (WebAssembly) | `wasm/openjpeg*` | BSD-2-Clause, `wasm/LICENSE_OPENJPEG`, `wasm/LICENSE_PDFJS_OPENJPEG` |
| JBIG2 decoder from PDFium (WebAssembly) | `wasm/jbig2*` | BSD-3-Clause and Apache-2.0, `wasm/LICENSE_JBIG2`, `wasm/LICENSE_PDFJS_JBIG2` |
| qcms colour management (WebAssembly) | `wasm/qcms_bg.wasm` | MIT, `wasm/LICENSE_QCMS`, `wasm/LICENSE_PDFJS_QCMS` |
| Adobe CMaps | `cmaps/*.bcmap` | BSD-3-Clause (Adobe), `cmaps/LICENSE` |
| ICC colour profile | `iccs/CGATS001Compat-v2-micro.icc` | CC0-1.0, `iccs/LICENSE` |
