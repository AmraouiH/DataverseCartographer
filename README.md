# Dataverse Cartographer

**Map your org's tables and relationships** — an XrmToolBox plugin that draws the data model of a Dataverse organization as an interactive graph: tables, 1:N / N:1 / N:N relationships, cascade behaviors, columns and keys. Understand a model in minutes, document a solution, or prepare an impact analysis before a change.

English / French.

## Features (v1)

| | |
|---|---|
| **Interactive graph** | Zoom (wheel, around the cursor), pan (drag the background), move tables by hand. Four automatic layouts: hierarchical (the « one » side above the « many » side), force-directed, circular / radial, grid. Edges that would cross a table are bent around it. |
| **Smart filters** | By solution, by publisher prefix (`mso_`…), custom / standard tables, hide system tables (`systemuser`, `team`, `activitypointer`, `annotation`…), hide tables without relationships, show or hide N:N and self-references. Intersect tables are never drawn: the N:N edge links both sides directly. |
| **Neighborhood exploration** | Pick a table (or double-click it on the map) and keep only its neighbors on 1 to 6 levels. Optionally show, faded, the linked tables excluded by the filters. *Back* returns to the previous focus. |
| **Relationship details** | Crow's-foot notation (bar = one, prongs = many), N:N dashed, **cascade-delete relationships in red**. Hover for a tooltip; click for the full card: schema name, lookup / referenced columns, Assign / Delete / Share / Unshare / Reparent / Merge / Rollup view. |
| **Table card** | Schema name, ownership, primary key / name, prefix, relationship counts, then *Columns* (type, lookup targets, required level, custom), *Relationships* (seen from the table) and *Keys* (alternate keys). Columns are loaded on demand in the background. |
| **Search** | Finds a table, a lookup column or a loaded column on the map; Enter cycles through the matches. A table that is not displayed can be explored straight from the search box. |
| **Exports** | PNG and SVG (the whole map with a title line), Mermaid `erDiagram` (`.mmd`, or `.md` with a fenced block) and PlantUML (`.puml`) for wikis and Git repositories, Excel inventory (tables, relationships with cascades, columns). Mermaid / PlantUML can also be copied to the clipboard. |

Hovering a table highlights its relationships and fades the rest; right-click a table to explore around it, center it, hide it or copy its names.

### Table logic (table card)

| Tab | Content |
|---|---|
| **Forms** | Every form of the table: type (main, quick create, quick view, card…), active / inactive, default form, number of libraries and handlers. |
| **JavaScript** | Every handler read from the form XML: library (web resource), event (OnLoad, OnSave, OnChange of a column, TabStateChange of a tab, control events), function, form. Libraries loaded without any handler are listed too. |
| **Plugins** | The steps registered on the table: message, stage, mode, rank, plugin type, filtering attributes, state. Double-click a step to open it in the plugin map. |

### Plugin map (« Plugins » tab)

A separate, technical map that reads as a chain: **table → (step) → plugin → (what its code does) → table**.
Example: `contact —[Create · Post]→ ContactPostCreate —[Update]→ account —[Create · Post]→ AccountPostCreate`.

* **Filters**: solution (plugin assemblies, types and steps of the solution), assembly (DLL), hide Microsoft / system plugins, include disabled steps, show reads.
* **Triggers** (table → plugin): the registered steps, grouped (message · stage), sync in blue, async in amber; global messages (custom APIs, actions without table) and service endpoints are shown too.
* **Actions** (plugin → table): found by a **static analysis of the plugin code**. The DLL stored in the organization (or the DLL inside a plugin package) is read with a built-in ECMA-335 metadata reader, without loading it. The IL is scanned for SDK calls (`IOrganizationService.Create / Update / Delete / Retrieve…`, `XxxRequest`, `OrganizationServiceContext`) and table names (`new Entity("account")`, early-bound classes with `[EntityLogicalName]`, `QueryExpression`, FetchXML, `Target = new EntityReference("incident", id)`), following the calls inside the assembly (helpers, services behind interfaces, base plugin class).
* **Details**: steps (with images and filtering attributes) and code operations (table, operation, method where it was found).
* Same interactions and exports as the data model map (PNG / SVG / Mermaid flowchart / PlantUML / Excel steps + operations).

### JavaScript map (« JavaScript » tab)

Same idea for the form scripts: **table → (form events) → library → (what its code calls) → table**.
Example: `account —[OnLoad, OnSave, OnChange (parentaccountid)]→ account.js —[Update]→ contact`.

* **Filters**: solution (forms and web resources of the solution — the library list then shows only its scripts), library, hide Microsoft libraries (msdyn_, mscrm, adx_…), include inactive forms, show reads and navigation.
* **Triggers** (table → library): the handlers of every form of the table (OnLoad, OnSave, OnChange of a column, TabStateChange…); libraries loaded without any handler are drawn dashed.
* **Calls** (library → table): found by reading the web resource: `Xrm.WebApi` (createRecord, updateRecord, deleteRecord, retrieveRecord, retrieveMultipleRecords, also through an alias), Web API URLs (`/api/data/v9.x/accounts` with the HTTP verb used), FetchXML, `openForm` / `navigateTo` / `lookupObjects`, custom API calls (`operationName`). Each call is attached to its function and line.

### Look and feel

Same command bar and « AH » icon as Entity Fields Analyser, loading screen with the animated « AH » icon, modern message dialogs, and hover cards on the maps (structured, colored, scrollable).

The code analysis is a heuristic: it cannot see table names built at runtime (configuration, parameters), and an entity built in one method then saved in another is linked and flagged as *inferred*. Assemblies stored on disk / GAC (on-premises) have no code to analyze: only their steps are drawn.

## How it reads the metadata

* One light `RetrieveMetadataChanges` query for the whole organization (only the table and relationship properties the map needs). If an environment rejects it, the tool falls back to `RetrieveAllEntities` (Entity + Relationships).
* Solutions (`isvisible = true`) and publishers feed the filters; the tables of a solution come from its `solutioncomponent` rows (component type 1) and are cached.
* Columns and keys of a table: `RetrieveEntity`, only when its card is opened (or for the Excel inventory, on request).

Nothing is written to the organization.

## Build

```
dotnet build -c Debug
```

.NET Framework 4.8, a single DLL with no dependency other than `XrmToolBoxPackage` (all the UI is drawn with GDI+). In Debug, the DLL is copied to `%AppData%\MscrmTools\XrmToolBox\Plugins` after each build (`-p:DeployToXrmToolBox=false` to skip it).

## Roadmap (v2)

* Saved views (table selection + positions), shareable with the team.
* Environment comparison (DEV / UAT / PROD): added, removed and changed tables and relationships.
* Impact analysis: « if I delete this column or relationship, what is touched? »
* Anomaly detection: orphan tables, risky cascade deletes on sensitive tables, unused lookups.

## Author

Hamza AMRAOUI — [LinkedIn](https://www.linkedin.com/in/hamza-amraoui/)
