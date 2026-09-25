# Project Workflows

> A member's role is stored in `project_members.role`. The role-to-permission mapping is fixed in application code, so separate role and permission tables are not required. Any authenticated user can create a project.

## Create project workflow

```mermaid
flowchart TD
    start([Start]) --> input[/Enter project details/]
    input -->|Submit| validate{"Project data valid?"}
    validate -->|No| validationError["Show validation errors"]
    validationError --> input

    validate -->|Yes| create["Create project"]
    create --> saved{"Saved successfully?"}
    saved -->|No| serverError["Show server error"]
    serverError --> input
    saved -->|Yes| activity["Record activity"]
    activity --> projectPage["Redirect to project page"]
    projectPage --> finish([End])
```

## Open project workflow

```mermaid
flowchart TD
    start([Start]) --> request["Open project"]
    request --> exists{"Project exists?"}
    exists -->|No| notFound["Show project not found"]
    notFound --> finish([End])

    exists -->|Yes| authenticated{"User logged in?"}
    authenticated -->|No| remember["Save project URL"]
    remember --> login["Redirect to login"]
    login --> request

    authenticated -->|Yes| member{"Is a project member?"}
    member -->|Yes| projectPage["Open project page"]
    member -->|No| denied["Show access denied"]
    denied --> dashboard["Redirect to dashboard"]

    projectPage --> finish
    dashboard --> finish
```

## Add project member workflow

```mermaid
flowchart TD
    start([Start]) --> input[/Enter member username/]
    input -->|Submit| validate{"Input valid?"}
    validate -->|No| validationError["Show validation errors"]
    validationError --> input

    validate -->|Yes| findUser{"User found?"}
    findUser -->|No| userError["Show user not found"]
    userError --> input

    findUser -->|Yes| existing{"Already a project member?"}
    existing -->|Yes| memberError["Show already-a-member message"]
    memberError --> input
    existing -->|No| addMember["Add project member"]
    addMember --> activity["Record activity"]
    activity --> notify["Notify the new member"]
    notify --> finish([End])
```
