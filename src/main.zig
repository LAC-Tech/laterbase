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
        left: ?*@This(),
        right: ?*@This(),

        fn init(allocator: std.mem.Allocator, k: K, v: V) !*@This() {
            var result = try allocator.create(@This());
            result.* = .{ .key = k, .val = v, .left = null, .right = null };
            return result;
        }
    };

    return struct {
        root: ?*Node,
        len: usize,

        const Self = @This();
        const Entry = struct { key_ptr: *K, value_ptr: *V };
        const ParentStack = std.ArrayListUnmanaged(?*Node);

        const Iterator = struct {
            parent_stack: ParentStack,
            current: ?*Node,

            pub fn init(bst: Self, allocator: std.mem.Allocator) !@This() {
                var parent_stack =
                    try ParentStack.initCapacity(allocator, bst.len);
                var current = bst.root;

                for (0..bst.len) |_| {
                    if (current) |c| {
                        if (c.left) |left| {
                            try parent_stack.append(allocator, current);
                            current = left;
                        } else {
                            break;
                        }
                    } else {
                        break;
                    }
                }

                return .{
                    .parent_stack = parent_stack,
                    .current = current,
                };
            }

            pub fn deinit(self: *@This(), allocator: std.mem.Allocator) void {
                self.parent_stack.deinit(allocator);
            }

            fn next_node(self: *@This()) ?*Node {
                if (self.current) |current| {
                    if (current.right == null) {
                        if (self.parent_stack.popOrNull()) |result| {
                            return result;
                        } else {
                            return null;
                        }
                    } else {
                        return current.right;
                    }
                } else {
                    return null;
                }
            }

            pub fn next(self: *@This()) ?Entry {
                if (self.next_node()) |n| {
                    self.current = n;
                    return .{ .key_ptr = &n.key, .value_ptr = &n.val };
                } else {
                    return null;
                }
            }
        };

        fn init() @This() {
            return .{ .root = null, .len = 0 };
        }

        fn put(
            self: *@This(),
            allocator: std.mem.Allocator,
            k: K,
            v: V,
        ) !void {
            var new_node = try Node.init(allocator, k, v);

            if (self.root) |root| {
                var current: *Node = root;

                for (0..self.len) |_| {
                    if (k < current.*.key) {
                        if (current.*.left) |left_node| {
                            current = left_node;
                        } else {
                            current.*.left = new_node;
                            break;
                        }
                    } else if (k > current.*.key) {
                        if (current.*.right) |right_node| {
                            current = right_node;
                        } else {
                            current.*.right = new_node;
                            break;
                        }
                    } else {
                        std.debug.panic("Attempted to add duplicate key {}", .{k});
                    }
                }
            } else {
                // Tree is empty!
                self.root = new_node;
            }

            self.len += 1;
        }

        fn get(self: @This(), k: K) ?V {
            if (self.root) |root| {
                var current: *Node = root;

                for (0..self.len) |_| {
                    if (k < current.*.key) {
                        if (current.*.left) |left_node| {
                            current = left_node;
                        } else {
                            return null;
                        }
                    } else if (k > current.*.key) {
                        if (current.*.right) |right_node| {
                            current = right_node;
                        } else {
                            return null;
                        }
                    } else {
                        break;
                    }
                }

                return current.val;
            }

            return null;
        }

        fn iterator(self: @This(), allocator: std.mem.Allocator) !Iterator {
            return try Iterator.init(self, allocator);
        }
    };
}

test "BST" {
    var arena = std.heap.ArenaAllocator.init(std.testing.allocator);
    defer arena.deinit();
    var bst = BST(u64, u64).init();
    try bst.put(arena.allocator(), 4, 2);
    try std.testing.expectEqual(@as(usize, 1), bst.len);
    try std.testing.expectEqual(@as(?u64, 2), bst.get(4));
    try std.testing.expectEqual(@as(?u64, null), bst.get(27));
    try bst.put(arena.allocator(), 0, 1);
    try bst.put(arena.allocator(), 10, 0);

    var iter = try bst.iterator(arena.allocator());
    defer iter.deinit(arena.allocator());

    try std.testing.expectEqual(@as(u64, 0), iter.next().?.key_ptr.*);
}
