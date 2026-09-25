# Task Workflows

> Task actions use the member role stored in `project_members.role`. The application maps that role to permissions; permissions are not stored per task.

## Create or edit task workflow

```mermaid
flowchart TD
    start([Start]) --> action{"Create or edit task?"}
    action -->|Create| role["Load project_members.role"]
    action -->|Edit| loadTask["Load current task data"]
    loadTask --> role

    role --> permission{"App permission allows selected task action?"}
    permission -->|No| denied["Show task access denied"]
    denied --> finish([End])
    permission -->|Yes| execute{"Selected action"}
    execute -->|Create| form[/Enter or edit task details/]
    execute -->|Edit| form

    form -->|Submit| validate{"Task data valid?"}
    validate -->|No| validationError["Show validation errors"]
    validationError --> form

    validate -->|Yes| save["Create or update task"]
    save --> saved{"Saved successfully?"}
    saved -->|No| serverError["Show server error"]
    serverError --> form
    saved -->|Yes| history["Record task history"]
    history --> details["Open task details"]
    details --> finish([End])
```

## Task-status workflow

```mermaid
flowchart TD
    start([Start]) --> task["Open task details"]
    task --> role["Load project_members.role"]
    role --> permission{"App permission allows status update?"}
    permission -->|No| denied["Show task access denied"]
    denied --> finish([End])
    permission -->|Yes| status[/Select task status/]
    status --> saveStatus["Save task status"]
    saveStatus --> history["Record task history"]
    history --> refresh["Refresh task details"]
    refresh --> finish([End])
```

## Comment workflow

```mermaid
flowchart TD
    start([Start]) --> task["Open task comments"]
    task --> role["Load project_members.role"]
    role --> action["Select comment action"]
    action --> permission{"App permission allows selected comment action?"}
    permission -->|No| unavailable["Action unavailable"]
    permission -->|Yes| execute{"Selected action"}
    execute -->|Add| input[/Enter comment/]
    execute -->|Delete| confirm{"Confirm deletion?"}

    input --> validate{"Comment valid?"}
    validate -->|No| validationError["Show validation error"]
    validationError --> input
    validate -->|Yes| save["Create comment"]

    unavailable --> finish([End])
    confirm -->|No| finish
    confirm -->|Yes| delete["Delete comment"]

    save --> activity["Record comment activity"]
    delete --> activity
    activity --> refresh["Refresh comments and activity history"]
    refresh --> finish
```
