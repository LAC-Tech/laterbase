// Data Structures

const std = @import("std");

// Binary Search Tree
// Implementing this because Zig doesn't have kind of sorted map
// Also, this is the simplest one I could think of

// TODO: "Performance Analysis of BSTs in System Software" (Pfaff, 2003)
// Paper suggets Splay trees or what we want
// AVL trees are second best, and do not modify tree when searching
pub fn BST(comptime K: type, comptime V: type) type {
    const Node = struct {
        key: K,
        val: V,
        left: ?*@This(),
        right: ?*@This(),
    };

    const FoundNode = union(enum) { first_gt: *Node, eq: *Node, not_found: void };

    return struct {
        root: ?*Node,
        len: usize,
        allocator: std.heap.MemoryPool(Node),

        const Self = @This();
        const Entry = struct { key_ptr: *K, value_ptr: *V };
        const ParentStack = std.ArrayListUnmanaged(?*Node);

        const Iterator = struct {
            parent_stack: ParentStack,
            current: ?*Node,

            pub fn init(
                allocator: std.mem.Allocator,
                node: ?*Node,
                initial_size: usize,
            ) !@This() {
                var parent_stack =
                    try ParentStack.initCapacity(allocator, initial_size);
                var current = node;

                for (0..initial_size) |_| {
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

        pub fn init(
            allocator: std.mem.Allocator,
            initial_size: usize,
        ) !@This() {
            return .{
                .root = null,
                .len = 0,
                .allocator = try std.heap.MemoryPool(Node).initPreheated(
                    allocator,
                    initial_size,
                ),
            };
        }

        pub fn deinit(self: *@This()) void {
            self.allocator.deinit();
        }

        pub fn put(
            self: *@This(),
            k: K,
            v: V,
        ) !void {
            var new_node = try self.allocator.create();
            new_node.* = .{ .key = k, .val = v, .left = null, .right = null };

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

        // Can be used for prefix scans
        fn find_node(self: @This(), k: K) FoundNode {
            var result: FoundNode = .{ .not_found = {} };

            if (self.root) |root| {
                var current: *Node = root;

                for (0..self.len) |_| {
                    if (current.*.key > k) {
                        // Keep track of what the latest key that's bigger is
                        result = .{ .first_gt = current };
                        if (current.*.left) |left_node| {
                            current = left_node;
                        } else {
                            // There's no smaller key
                            break;
                        }
                    } else if (k > current.*.key) {
                        if (current.*.right) |right_node| {
                            current = right_node;
                        } else {
                            // there's no larger key;
                            break;
                        }
                    } else {
                        result = .{ .eq = current };
                        break;
                    }
                }
            }

            return result;
        }

        pub fn get(self: @This(), k: K) ?V {
            return switch (self.find_node(k)) {
                .eq => |node| node.val,
                else => null,
            };
        }

        pub fn iterator(
            self: @This(),
            allocator: std.mem.Allocator,
        ) !Iterator {
            return try Iterator.init(allocator, self.root, self.len);
        }

        pub fn iterator_from(
            self: @This(),
            allocator: std.mem.Allocator,
            k: K,
        ) !Iterator {
            var node = self.find_node(k);
            // TODO: better 'length' heurestic than the number of nodes in tree
            // This will over allocate
            return try Iterator.init(allocator, node, self.len);
        }
    };
}
