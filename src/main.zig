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

        // const Entry = struct { key_ptr: *K, value_ptr: *V };
        // _ = Entry;

        const Iterator = struct {
            node: ?*Node,

            pub fn init(bst: Self) @This() {
                var node: ?*Node = null;
                node = bst.root;
                if (node) |n| {
                    while (n.*.left) |left| {
                        node = left;
                    }
                }
                return .{ .node = node };
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

        fn iterator(self: @This()) Iterator {
            return Iterator.init(self);
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

    //try bst.put(arena.allocator(), 0, 1);
    //var iterator = bst.iterator();
    //_ = iterator;

    // try std.testing.expectEqual(@as(*const u64, &0), iterator.next().?.key_ptr);
    // try std.testing.expectEqual(@as(*const u64, &4), iterator.next().?.key_ptr);
}
