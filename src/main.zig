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
        const IdIndex = ds.BST(inter.EventId, u64);

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

        fn read(self: @This(), query: Query) []const inter.Event(Payload) {
            switch (query.time) {
                .logical_txn => self.log.items[query.limit..],
                // .physical_valid => {
                //     self.id_index.iterator_from(query., k: K)

                //     // eventIdIndex
                //     // |> Seq.skip query.Limit
                //     // |> Seq.map (fun (k, i) -> (k, snd log[i]))
                // }
            }
        }
    };
}

test "BST" {
    var bst = try ds.BST(u64, u64).init(std.testing.allocator, 8);
    defer bst.deinit();

    try std.testing.expectEqual(@as(usize, 0), bst.len);

    // TODO: some kind of 'from iterator' ? how do std lib containers do it?
    try bst.put(4, 2);
    try bst.put(0, 1);
    try bst.put(10, 0);
    try bst.put(2, 20);

    try std.testing.expectEqual(@as(usize, 4), bst.len);
    try std.testing.expectEqual(@as(?u64, 2), bst.get(4));
    try std.testing.expectEqual(@as(?u64, null), bst.get(27));

    // Try and see what the shape of the tree is
    try std.testing.expectEqual(@as(u64, 2), bst.root.?.val);
    try std.testing.expectEqual(@as(u64, 1), bst.root.?.left.?.val);
    try std.testing.expectEqual(@as(u64, 0), bst.root.?.right.?.val);

    var iter = try bst.iterator(std.testing.allocator);
    defer iter.deinit(std.testing.allocator);

    try std.testing.expectEqual(@as(u64, 0), iter.next().?.key_ptr.*);
    try std.testing.expectEqual(@as(u64, 2), iter.next().?.key_ptr.*);
    try std.testing.expectEqual(@as(u64, 4), iter.next().?.key_ptr.*);
    try std.testing.expectEqual(@as(u64, 10), iter.next().?.key_ptr.*);

    var iter2 = try bst.iterator_from(std.testing.allocator, 1);
    defer iter2.deinit(std.testing.allocator);

    try std.testing.expectEqual(@as(u64, 2), iter2.next().?.key_ptr.*);
    //try std.testing.expectEqual(@as(u64, 4), iter2.next().?.key_ptr.*);
    //try std.testing.expectEqual(@as(u64, 10), iter2.next().?.key_ptr.*);
}

test "Logical Clock" {
    var lc = try time.LogicalClock.init(std.testing.allocator);
    defer lc.deinit(std.testing.allocator);
}

test "Local Replica" {
    var r = try LocalReplica(u64).init(std.testing.allocator);
    defer r.deinit();
}
