const std = @import("std");

pub fn main() void {
    std.debug.print("gettin ziggy wit it", .{});
}

// Binary Search Tree
// Implementing this because Zig doesn't have kind of sorted map
// Also, this is the simplest one I could think of
// Plan to use this for indices
fn BST(comptime K: type, comptime V: type) type {
    const Node = struct {
        key: K,
        val: V,
        left: *?@This(),
        right: *?@This(),
    };

    return struct {
        root: ?*Node,

        fn init() @This() {
            return .{ .root = null };
        }
    };
}

test "BST" {
    _ = BST(u64, u64).init();
}
