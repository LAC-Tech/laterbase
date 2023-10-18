const std = @import("std");
const ds = @import("ds.zig");
const inter = @import("inter.zig");
const time = @import("time.zig");

pub fn main() void {
    std.debug.print("gettin ziggy wit it", .{});
}

const Query = union(time.Type) {
    logical_txn: u64,
    physical_valid: i64,
};

fn LocalReplica(comptime Payload: type) type {
    return struct {
        const maxNumEvents = 10_000;

        const Log = std.ArrayListUnmanaged(inter.Event(Payload));
        const IdIndex = ds.BST(inter.EventId, u64, inter.EventId.order);

        // The log is the source of truth. Everything else is just a cache!
        log: Log,
        // This is an "operational index" - needed for a replica to work.
        // Used to prevent adding duplicates to the log, and for read queries
        // It is not a source of truth - only the log is
        id_index: IdIndex,
        // Each replica should have its own storage
        allocator: std.mem.Allocator,

        fn init(allocator: std.mem.Allocator) !@This() {
            return .{
                .log = try Log.initCapacity(allocator, maxNumEvents),
                .id_index = try IdIndex.init(allocator, maxNumEvents),
                .allocator = allocator,
            };
        }

        fn deinit(self: *@This()) void {
            self.log.deinit(self.allocator);
            self.id_index.deinit();
        }

        fn read(
            self: @This(),
            comptime B: fn (comptime type) type,
            buf: *B(inter.Event(Payload)),
            query: Query,
        ) !void {
            switch (query) {
                .logical_txn => |n| {
                    var events = self.log.items[n..];
                    try buf.appendSlice(events);
                },
                .physical_valid => |n| {
                    var prefix_key = inter.EventId.init(n, 0);
                    var it = try self.id_index.iteratorFrom(
                        self.allocator,
                        prefix_key,
                    );
                    defer it.deinit(self.allocator);

                    while (it.next()) |entry| {
                        var index = entry.value_ptr.*;
                        try buf.append(self.log.items[index]);
                    }
                },
            }
        }
    };
}

const expectEqual = std.testing.expectEqual;

test "Logical Clock" {
    var lc = try time.LogicalClock.init(std.testing.allocator);
    defer lc.deinit(std.testing.allocator);
}

test "Local Replica" {
    var r = try LocalReplica(u8).init(std.testing.allocator);
    defer r.deinit();

    var events = std.ArrayList(inter.Event(u8)).init(std.testing.allocator);
    defer events.deinit();

    try r.read(std.ArrayList, &events, .{ .logical_txn = 0 });

    try expectEqual(@as(u64, 0), events.items.len);
}
