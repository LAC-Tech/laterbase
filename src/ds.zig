// Data Structures

const std = @import("std");

// Binary Search Tree
// Implementing this because Zig doesn't have kind of sorted map
// Also, this is the simplest one I could think of
pub fn BST(comptime K: type, comptime V: type) type {
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
                        }
                        return null;
                    }
                    return current.right;
                }
                return null;
            }

            pub fn next(self: *@This()) ?Entry {
                var result: ?Entry = null;
                if (self.current) |current| {
                    result = .{
                        .key_ptr = &current.key,
                        .value_ptr = &current.val,
                    };
                }

                if (self.next_node()) |n| {
                    self.current = n;
                }

                return result;
            }
        };

        pub fn init() @This() {
            return .{ .root = null, .len = 0 };
        }

        pub fn put(
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

        pub fn get(self: @This(), k: K) ?V {
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

        pub fn iterator(self: @This(), allocator: std.mem.Allocator) !Iterator {
            return try Iterator.init(self, allocator);
        }
    };
}
