# Implementation Specification

> **Current v0.9.1 supersession:** About displays product version `0.9.1` and labelled, copyable vetted HTTPS addresses. It exposes no inert link button and does not launch a browser, process, or shell. The external-link request contract below records the historical v0.1 foundation.

| Property | Value |
|----------|-------|
| Spec ID | 022 |
| Component | About View |
| Project | OpenSorSe.Desktop |
| Version | 1.0 |
| Target Release | v0.1 |
| Status | Draft |

---

# Purpose

The About View displays information about OpenSorSe, including the application version, licensing, authorship, acknowledgements, and useful project links.

---

# Why

Users should be able to easily identify the version of OpenSorSe they are using, understand its licensing, and find official project resources.

For an open-source project, this view also provides a clear entry point for contributors.

---

# Responsibilities

The About View shall:

- Display the application name.
- Display the application version.
- Display copyright information.
- Display the project license.
- Display acknowledgements.
- Provide links to the project repository.
- Provide links to documentation.

---

# Does NOT

The About View must NOT:

- Modify application settings.
- Execute business logic.
- Access the filesystem.
- Scan folders.
- Move files.
- Execute rules.

---

# Inputs

- Application metadata.
- Version information.

---

# Outputs

The component provides:

- External link requests.
- Navigation events.

---

# Workflow

1. Open the About View.
2. Load application information.
3. Display version and license.
4. Display acknowledgements.
5. Open external links when requested.
6. Return to the previous view.

---

# Assumptions

- Application metadata is available.
- Internet access is optional for opening links.

---

# Acceptance Criteria

The implementation is complete when:

- Application information is displayed.
- Version information is correct.
- License information is displayed.
- Repository link opens successfully.
- Documentation link opens successfully.
- UI tests pass.

---

# Layout

+------------------------------------------------------------+
| About OpenSorSe                                             |
+------------------------------------------------------------+

                OpenSorSe

Version

v0.1.0

------------------------------------------------------------

License

MIT License

------------------------------------------------------------

Developed by

OpenSorSe Labs

------------------------------------------------------------

Links

[ GitHub Repository ]

[ Documentation ]

[ Report Issue ]

------------------------------------------------------------

[ Close ]

------------------------------------------------------------

Status

Ready

---

# Future

Not part of v0.1:

- Contributor list.
- Release notes.
- Changelog viewer.
- Sponsor links.
- Community links.

---

# Dependencies

Depends on:

- 013 - Main Window

Required by:

- None

---

# Autonomous v0.1 Decisions

The draft does not define a metadata provider, version source, repository URI, documentation URI, or link-launching contract. v0.1 declares application version `0.1.0` in the Desktop project and presents static matching metadata. Repository and documentation links are represented as vetted HTTPS `Uri` values and emitted as external-link requests; the view never launches a process or requires internet access.

The implementation specification declares the MIT license, which is displayed as supplied. A packaged license file, contributor list, changelog, release notes, link-opening host policy, and actual network access remain deferred.
