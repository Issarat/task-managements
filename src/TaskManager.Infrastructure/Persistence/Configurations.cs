using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskManager.Domain.Entities;
using TaskManager.Domain.Enums;

namespace TaskManager.Infrastructure.Persistence;

internal sealed class AnonymousUserConfiguration : IEntityTypeConfiguration<AnonymousUser>
{
    public void Configure(EntityTypeBuilder<AnonymousUser> builder)
    {
        builder.ToTable("anonymous_users");
        builder.HasKey(x => x.Id).HasName("PK_anonymous_users");

        builder.Property(x => x.Id).HasColumnName("anonymous_user_id").ValueGeneratedNever();
        builder.Property(x => x.SessionTokenHash).HasColumnName("session_token_hash").HasColumnType("char(64)").IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(80).IsRequired();
        builder.Property(x => x.AvatarColor).HasColumnName("avatar_color").HasColumnType("char(7)").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.LastSeenAtUtc).HasColumnName("last_seen_at_utc").HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc").HasColumnType("datetime2");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsRowVersion();

        builder.HasIndex(x => x.SessionTokenHash).IsUnique().HasDatabaseName("UX_anonymous_users_session_token_hash");
        builder.HasIndex(x => x.ExpiresAtUtc).HasDatabaseName("IX_anonymous_users_expires_at_utc");
    }
}

internal sealed class BoardConfiguration : IEntityTypeConfiguration<Board>
{
    public void Configure(EntityTypeBuilder<Board> builder)
    {
        builder.ToTable(
            "boards",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("CK_boards_last_activity", "[last_activity_at_utc] >= [created_at_utc]");
                tableBuilder.HasCheckConstraint("CK_boards_expiry", "[expires_at_utc] > [created_at_utc]");
            });

        builder.HasKey(x => x.Id).HasName("PK_boards");

        builder.Property(x => x.Id).HasColumnName("board_id").ValueGeneratedNever();
        builder.Property(x => x.PublicId).HasColumnName("public_id").HasMaxLength(32).IsUnicode(false).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(x => x.OwnerTokenHash).HasColumnName("owner_token_hash").HasColumnType("char(64)").IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("datetime2");
        builder.Property(x => x.LastActivityAtUtc).HasColumnName("last_activity_at_utc").HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc").HasColumnType("datetime2");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsRowVersion();

        builder.HasIndex(x => x.PublicId).IsUnique().HasDatabaseName("UX_boards_public_id");
        builder.HasIndex(x => x.OwnerTokenHash).IsUnique().HasDatabaseName("UX_boards_owner_token_hash");
        builder.HasIndex(x => x.ExpiresAtUtc).HasDatabaseName("IX_boards_expires_at_utc");
    }
}

internal sealed class BoardParticipantConfiguration : IEntityTypeConfiguration<BoardParticipant>
{
    public void Configure(EntityTypeBuilder<BoardParticipant> builder)
    {
        builder.ToTable("board_participants");
        builder.HasKey(x => x.Id).HasName("PK_board_participants");
        builder.HasAlternateKey(x => new { x.Id, x.BoardId })
            .HasName("AK_board_participants_participant_board");

        builder.Property(x => x.Id).HasColumnName("board_participant_id").ValueGeneratedNever();
        builder.Property(x => x.BoardId).HasColumnName("board_id");
        builder.Property(x => x.AnonymousUserId).HasColumnName("anonymous_user_id");
        builder.Property(x => x.JoinedAtUtc).HasColumnName("joined_at_utc").HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.LastSeenAtUtc).HasColumnName("last_seen_at_utc").HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsRowVersion();

        builder.HasOne(x => x.Board)
            .WithMany(x => x.Participants)
            .HasForeignKey(x => x.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.AnonymousUser)
            .WithMany(x => x.BoardParticipants)
            .HasForeignKey(x => x.AnonymousUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.BoardId, x.AnonymousUserId })
            .IsUnique()
            .HasDatabaseName("UX_board_participants_board_user");
        builder.HasIndex(x => new { x.BoardId, x.LastSeenAtUtc })
            .HasDatabaseName("IX_board_participants_board_last_seen");
    }
}

internal sealed class BoardColumnConfiguration : IEntityTypeConfiguration<BoardColumn>
{
    public void Configure(EntityTypeBuilder<BoardColumn> builder)
    {
        builder.ToTable("board_columns");
        builder.HasKey(x => x.Id).HasName("PK_board_columns");
        builder.HasAlternateKey(x => new { x.Id, x.BoardId })
            .HasName("AK_board_columns_column_board");

        builder.Property(x => x.Id).HasColumnName("board_column_id").ValueGeneratedNever();
        builder.Property(x => x.BoardId).HasColumnName("board_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
        builder.Property(x => x.ColumnKey).HasColumnName("column_key").HasMaxLength(40).IsUnicode(false).IsRequired();
        builder.Property(x => x.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("datetime2");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsRowVersion();

        builder.HasOne(x => x.Board)
            .WithMany(x => x.Columns)
            .HasForeignKey(x => x.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.BoardId, x.ColumnKey })
            .IsUnique()
            .HasDatabaseName("UX_board_columns_board_key");
        builder.HasIndex(x => new { x.BoardId, x.SortOrder })
            .HasDatabaseName("IX_board_columns_board_sort");
    }
}

internal sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable(
            "task_items",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("CK_task_items_priority", "[priority] IN ('Low', 'Medium', 'High')");
                tableBuilder.HasCheckConstraint("CK_task_items_sort_order", "[sort_order] >= 0");
                tableBuilder.HasCheckConstraint("CK_task_items_dates", "[start_date] IS NULL OR [due_date] IS NULL OR [start_date] <= [due_date]");
            });

        builder.HasKey(x => x.Id).HasName("PK_task_items");

        builder.Property(x => x.Id).HasColumnName("task_item_id").ValueGeneratedNever();
        builder.Property(x => x.BoardId).HasColumnName("board_id");
        builder.Property(x => x.BoardColumnId).HasColumnName("board_column_id");
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(60).IsRequired();
        builder.Property(x => x.DescriptionMarkdown).HasColumnName("description_markdown").HasColumnType("nvarchar(max)").HasMaxLength(20_000);
        builder.Property(x => x.Priority)
            .HasColumnName("priority")
            .HasMaxLength(20)
            .IsUnicode(false)
            .HasConversion<string>()
            .HasDefaultValue(TaskPriority.Medium)
            .HasSentinel((TaskPriority)0);
        builder.Property(x => x.SortOrder).HasColumnName("sort_order").HasPrecision(18, 6).HasDefaultValue(0m);
        builder.Property(x => x.AssignedToParticipantId).HasColumnName("assigned_to_participant_id");
        builder.Property(x => x.StartDate).HasColumnName("start_date").HasColumnType("date");
        builder.Property(x => x.DueDate).HasColumnName("due_date").HasColumnType("date");
        builder.Property(x => x.CreatedByParticipantId).HasColumnName("created_by_participant_id");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("datetime2").HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").HasColumnType("datetime2");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsRowVersion();

        builder.HasOne(x => x.Board)
            .WithMany(x => x.Tasks)
            .HasForeignKey(x => x.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.BoardColumn)
            .WithMany(x => x.Tasks)
            .HasForeignKey(x => new { x.BoardColumnId, x.BoardId })
            .HasPrincipalKey(x => new { x.Id, x.BoardId })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.CreatedByParticipant)
            .WithMany(x => x.CreatedTasks)
            .HasForeignKey(x => new { x.CreatedByParticipantId, x.BoardId })
            .HasPrincipalKey(x => new { x.Id, x.BoardId })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne(x => x.AssignedToParticipant)
            .WithMany(x => x.AssignedTasks)
            .HasForeignKey(x => new { x.AssignedToParticipantId, x.BoardId })
            .HasPrincipalKey(x => new { x.Id, x.BoardId })
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.BoardId, x.BoardColumnId, x.SortOrder })
            .HasDatabaseName("IX_task_items_board_column_sort");
        builder.HasIndex(x => new { x.BoardId, x.AssignedToParticipantId })
            .HasDatabaseName("IX_task_items_board_assignee");
    }
}
