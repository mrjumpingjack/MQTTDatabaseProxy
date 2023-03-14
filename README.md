# MQTTDatabaseProxy

### Globalconfig

-   `ip`: The IP address of the MQTT broker that the application will connect to.
-   `port`: The port number of the MQTT broker.
-   `user`: The username used to authenticate with the MQTT broker.
-   `password`: The password used to authenticate with the MQTT broker.
-   `userPrefix`: A prefix that will be added with to topic to the beginning of the MQTT client ID that the application will use when connecting to the broker.
-   `DatabaseUri`: The URI of the database that the application will use to store the data.
-   `DatabaseUser`: The username used to authenticate with the database.
-   `DatabasePassword`: The password used to authenticate with the database.
-   `DatabaseName`: The name of the database that the application will use to store the data.

### Topics

-   `name`: The MQTT topic that the application will subscribe to.
-   `ip`: The IP address of the MQTT broker that this topic will connect to (optional, will use the global IP address if not provided).
-   `port`: The port number of the MQTT broker that this topic will connect to (optional, will use the global port number if not provided).
-   `user`: The username used to authenticate with the MQTT broker (optional, will use the global username if not provided).
-   `password`: The password used to authenticate with the MQTT broker (optional, will use the global password if not provided).
-   `DBTable`: The name of the database table that the data from this topic will be stored in.
-   `ignore`: An array of MQTT topic sub-paths that the application will ignore when processing messages from this topic (optional).
-   `contenttype`: The format of the message payload (either "json" or "value").
-   `mappings`: An array of mappings that define how the data from this topic will be stored in the database.
-   `TimeOut`: An object that defines a timeout for this topic (optional). If a message is not received within the specified timeout period, the application will assume that the device sending the message is offline and will update the database accordingly.
-   `Regex`: A regular expression pattern that is used to filter and parse the payload of the MQTT message (optional).

### Mappings

-   `DBTable`: The name of the database table that the data from this mapping will be stored in.
-   `Input`: The MQTT topic sub-path that the mapping applies to.
-   `Output`: The database column names that the data from the MQTT message will be stored in. The "-" character is used to ignore parts of the topic.
-   `Extras`: An array of additional fields that should be added to the database record (optional).
-   `Override`: If set to "true", the value of the "Target" field will be overwritten with the value specified in the "value" field (optional).
-   `Target`: The name of the field that should be added to the database record (optional).
-   `Value`: The value that should be added to the database record.
-   `JSONMappings`: An array of mappings that apply to JSON-formatted MQTT messages. This field is only applied to mappings that have "contenttype" set to "json".
-   `JSONMappingCreterias`: An array of conditions that are used to extract data from the JSON payload of the MQTT message. Each condition specifies a "Target" field in the database record and a JSON path that should be used to extract the data from the message payload. The extracted data will be stored in the database field specified by the "Target" field.
