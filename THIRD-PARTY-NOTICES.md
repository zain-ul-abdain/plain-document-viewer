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

Source: https://github.com/mozilla/pdf.js, npm package pdfjs-dist 6.3.289 (integrity sha512-ZHjSVpDa3D6izMq8/04lvkhkATUmL9px6ChPaXc1k6nU2Mrhlg1/7F0bdUqCwUjw3NsPTfPZsMDUU6ZIcRaeQw==). Unmodified files are shipped in `Assets/pdf/pdfjs`, except that the four Liberation Sans font files are replaced (see the table below).

Copyright Mozilla Foundation. Licensed under the Apache License, Version 2.0; the full licence text ships as `Assets/pdf/pdfjs/LICENSE`. You may not use these files except in compliance with the License. Distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND.

Components bundled inside PDF.js, each with its licence file next to it:

| Component | Files | Licence |
|---|---|---|
| Foxit standard fonts (from PDFium) | `standard_fonts/Foxit*.pfb` | BSD-3-Clause, `standard_fonts/LICENSE_FOXIT` |
| Liberation Sans 2.1.5 (replaces the 1.07.4 files PDF.js ships, which were GPL v2 with a font exception) | `standard_fonts/LiberationSans-*.ttf`, unmodified copies from LibreOffice 26.2.6 | SIL Open Font License 1.1; copyright notice and full licence in `standard_fonts/LICENSE_LIBERATION`. Digitized data copyright (c) 2010 Google Corporation; copyright (c) 2012 Red Hat, Inc. SHA-256 of Regular: 76d04c18ea243f426b7de1f3ad208e927008f961dc5945e5aad352d0dfde8ee8. PDF.js loads them only when a PDF uses an unembedded Helvetica and system fonts are unavailable |
| OpenJPEG decoder (WebAssembly) | `wasm/openjpeg*` | BSD-2-Clause, `wasm/LICENSE_OPENJPEG`, `wasm/LICENSE_PDFJS_OPENJPEG` |
| JBIG2 decoder from PDFium (WebAssembly) | `wasm/jbig2*` | BSD-3-Clause and Apache-2.0, `wasm/LICENSE_JBIG2`, `wasm/LICENSE_PDFJS_JBIG2` |
| qcms colour management (WebAssembly) | `wasm/qcms_bg.wasm` | MIT, `wasm/LICENSE_QCMS`, `wasm/LICENSE_PDFJS_QCMS` |
| Adobe CMaps | `cmaps/*.bcmap` | BSD-3-Clause (Adobe), `cmaps/LICENSE` |
| ICC colour profile | `iccs/CGATS001Compat-v2-micro.icc` | CC0-1.0, `iccs/LICENSE` |

## ExcelNumberFormat 1.1.0

Source: https://github.com/andersnm/ExcelNumberFormat, NuGet package ExcelNumberFormat 1.1.0 (used by the worker to show cell values with Excel number formats).

The MIT License (MIT)

Copyright (c) 2017 andersnm

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

## LibreOffice 26.2.6 (document converter for Word and PowerPoint files)

Development builds use an unmodified copy unpacked by `scripts/fetch-libreoffice.ps1` from the official Windows x86-64 installer (SHA-256 `f9877032fd908beb9c0ddf06df4af5c2e85f419c42e14876c4cce5aae5fb2660`, as published by download.documentfoundation.org). It is run as a separate program and is not linked into Plain Viewer.

LibreOffice is licensed under the Mozilla Public License, version 2.0, with parts under the GNU Lesser General Public License v3+, the Apache License 2.0 and other open-source licences; see https://www.libreoffice.org/about-us/licenses/. Its own licence texts and third-party notices ship with it (`license.txt`, `LICENSE.html`, `NOTICE`, and the `readmes` folder).

Source code for this exact version is available from The Document Foundation at https://download.documentfoundation.org/libreoffice/src/26.2.6/ . When LibreOffice is included in a Plain Viewer installer, this notice, its licence files and this source location must be distributed with it (MPL-2.0 section 3.2).

Fonts copied from LibreOffice's own font folder (for example Carlito, Caladea, Liberation, DejaVu and Amiri) keep their licences, which are included in LibreOffice's `readmes` and licence files.
