# Project provenance

Halloweb grew from a small user-supplied HTML/SVG spider that avoids the pointer into a native Windows desktop pet. The human project direction, character idea, and supplied artwork came from the project's creator. Codex implemented and verified the native Windows overlay and later behavior changes. Gemini contributed JavaScript behavior prototypes that informed the independent regression work.

The standalone spider SVG is extracted from the initial user-supplied HTML; its drawing is also recreated by the native renderer. The project uses the MIT license selected by the creator for the Live-Spider repository.

The native application uses Windows Forms, System.Drawing, and Windows APIs from the installed Windows .NET Framework. It does not bundle an external browser, Electron runtime, font, JavaScript CDN, icon library, or other downloaded runtime dependency.

The JavaScript behavior engine and regression tests are maintained alongside the native implementation. They are separate hosts: JavaScript supports geometric detours, while the native app uses conservative safe waiting. They share the same measured-arrival and weaving-authorization principles.

No personal screen recording, desktop screenshot, session ledger, local file path, or development-chat export is part of the proposed public source tree.
